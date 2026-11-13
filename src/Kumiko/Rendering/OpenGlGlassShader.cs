namespace Kumiko.Rendering;

internal static class OpenGlGlassShader
{
    internal const string PassVertex = """
        #version 330 core
        void main() {
            vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);
            gl_Position=vec4(p*2.0-1.0,0,1);
        }
        """;

    internal const string Down = """
        #version 330 core
        uniform sampler2D source;
        uniform vec4 area;
        uniform int down;
        uniform int frameHeight;
        out vec4 outputColor;
        void main() {
            ivec2 start=ivec2(area.xy)+ivec2(gl_FragCoord.xy)*down;
            ivec2 end=min(start+ivec2(down),ivec2(area.zw));
            vec3 sum=vec3(0);
            for(int y=start.y;y<end.y;y++)
                for(int x=start.x;x<end.x;x++)
                    sum+=texelFetch(source,ivec2(x,frameHeight-1-y),0).rgb;
            outputColor=vec4(sum/float((end.x-start.x)*(end.y-start.y)),1);
        }
        """;

    internal const string Blur = """
        #version 330 core
        uniform sampler2D source;
        uniform vec2 size;
        uniform vec2 direction;
        uniform int radius;
        uniform float sigma;
        out vec4 outputColor;
        void main() {
            ivec2 at=ivec2(gl_FragCoord.xy);
            vec3 sum=vec3(0);
            float total=0.0;
            for(int k=-radius;k<=radius;k++) {
                ivec2 p=clamp(at+ivec2(direction)*k,ivec2(0),ivec2(size)-1);
                float w=exp(-float(k*k)/(2.0*sigma*sigma));
                sum+=texelFetch(source,p,0).rgb*w;
                total+=w;
            }
            outputColor=vec4(sum/total,1);
        }
        """;

    internal const string Fragment = """
        #version 330 core
        uniform sampler2D source;
        uniform vec2 origin;
        uniform vec2 size;
        uniform float down;
        uniform float scale;
        in vec2 world;
        in vec2 local;
        flat in vec4 irect;
        flat in vec4 icolor;
        flat in vec4 iborder;
        flat in vec4 iclip;
        flat in vec4 iparams;
        flat in vec4 iuv;
        out vec4 outputColor;
        vec2 distanceGradient(vec2 p,vec2 h,float r) {
            vec2 q=abs(p)-h+r,m=max(q,0.0);
            float len=length(m);
            return vec2(len+min(max(q.x,q.y),0.0)-r,len>0.0?(m.x+m.y)/len:1.0);
        }
        vec3 at(ivec2 p) {
            return texelFetch(source,clamp(p,ivec2(0),ivec2(size)-1),0).rgb;
        }
        vec3 sampleBackdrop(vec2 q) {
            vec2 u=(q-origin)/down-0.5,f=floor(u),w=u-f;
            ivec2 p=ivec2(f);
            return mix(mix(at(p),at(p+ivec2(1,0)),w.x),
                mix(at(p+ivec2(0,1)),at(p+ivec2(1,1)),w.x),w.y);
        }
        void main() {
            if(world.x<iclip.x||world.y<iclip.y||world.x>=iclip.z||world.y>=iclip.w) discard;
            float cut=1.0;
            if(iparams.w>0.0) {
                uint corners=uint(floor(iparams.w/4096.0));
                float radius=iparams.w-float(corners)*4096.0;
                vec2 h=(iclip.zw-iclip.xy)*0.5,q=world-iclip.xy-h;
                vec2 dg=distanceGradient(q,h,min(radius,min(h.x,h.y)));
                float aa=max(dg.y/scale,0.0001)*0.5;
                uint bit=q.y<0.0?(q.x<0.0?1u:2u):(q.x<0.0?8u:4u);
                if((corners&bit)!=0u) cut=1.0-smoothstep(-aa,aa,dg.x);
            }
            vec2 h=irect.zw*0.5,q=local-h;
            float r=min(iparams.x,min(h.x,h.y));
            vec2 dg=distanceGradient(q,h,r);
            float aa=max(dg.y/scale,0.0001)*0.5;
            float coverage=(1.0-smoothstep(-aa,aa,dg.x))*cut*iuv.z;
            if(coverage<=0.0) discard;
            float depth=max(0.0,-dg.x),bezel=iuv.x;
            vec2 normal=vec2(0);
            if(depth<bezel||depth<3.0*iuv.w) {
                float rr=min(r*1.5,min(h.x,h.y));
                vec2 a=abs(q)-h+rr;
                if(a.x>0.0||a.y>0.0) normal=normalize(max(a,0.0))*sign(q);
                else normal=a.x>a.y?vec2(sign(q.x),0):vec2(0,sign(q.y));
            }
            float bend=0.0;
            if(depth<bezel&&bezel>0.0) {
                float u=1.0-depth/bezel,v=1.0-u*u*u*u;
                float a=sqrt(v*sqrt(v)),b=u*u*u,n=sqrt(a*a+b*b);
                float sn=b/n,cs=a/n,st=sn/1.5,ct=sqrt(1.0-st*st);
                bend=iuv.y*(sn*ct-cs*st)/(cs*ct+sn*st)/1.118034;
            }
            vec3 c=sampleBackdrop((world-normal*bend)*scale);
            float l=dot(c,vec3(0.2126,0.7152,0.0722));
            float level=iborder.x+(iborder.y-iborder.x)*(1.0-pow(max(1.0-l,0.0),iborder.z));
            c=clamp(vec3(level)+(c-vec3(l))*((iborder.y-iborder.x)*iborder.w),0.0,1.0);
            c=mix(c,icolor.rgb,icolor.a);
            if(depth<3.0*iuv.w) {
                float v=depth/iuv.w;
                c=mix(c,vec3(abs(normal.y)),0.35*exp(-v*v));
            }
            outputColor=vec4(c*coverage,coverage);
        }
        """;
}
