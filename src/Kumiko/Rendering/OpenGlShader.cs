namespace Kumiko.Rendering;

internal static class OpenGlShader
{
    internal const string Vertex = """
        #version 330 core
        layout(location=0) in vec4 rect;
        layout(location=1) in vec4 color;
        layout(location=2) in vec4 border;
        layout(location=3) in vec4 uv;
        layout(location=4) in vec4 clip;
        layout(location=5) in vec4 params;
        layout(location=6) in vec4 turn;
        uniform vec2 viewport;
        out vec2 world;
        out vec2 local;
        out vec2 texel;
        flat out vec4 irect;
        flat out vec4 icolor;
        flat out vec4 iborder;
        flat out vec4 iclip;
        flat out vec4 iparams;
        flat out vec4 iuv;
        void main() {
            vec2 corner=vec2(gl_VertexID & 1, gl_VertexID >> 1);
            float grow=params.z==2.0 ? params.y*3.0 : 0.0;
            vec2 p=rect.xy-grow+corner*(rect.zw+2.0*grow);
            // Clipping and shading stay in the upright frame; only the position turns.
            vec2 q=p;
            if(turn.x!=0.0) {
                float s=sin(turn.x),c=cos(turn.x);
                vec2 d=p-turn.yz;
                q=turn.yz+vec2(d.x*c-d.y*s,d.x*s+d.y*c);
            }
            gl_Position=vec4(q/viewport*vec2(2,-2)+vec2(-1,1),0,1);
            world=p; local=p-rect.xy; texel=mix(uv.xy,uv.zw,corner);
            irect=rect; icolor=color; iborder=border; iclip=clip; iparams=params;
            iuv=uv;
        }
        """;
    internal const string Fragment = """
        #version 330 core
        uniform sampler2D atlas;
        in vec2 world;
        in vec2 local;
        in vec2 texel;
        flat in vec4 irect;
        flat in vec4 icolor;
        flat in vec4 iborder;
        flat in vec4 iclip;
        flat in vec4 iparams;
        out vec4 outputColor;
        float rounded(vec2 p,vec2 h,float r) {
            vec2 q=abs(p)-h+r;
            return length(max(q,0.0))+min(max(q.x,q.y),0.0)-r;
        }
        vec2 erf2(vec2 x) {
            vec2 a=abs(x),s=sign(x);
            vec2 v=1.0+(0.278393+(0.230389+0.078108*a*a)*a)*a;
            v*=v;
            return s-s/(v*v);
        }
        float shadow(vec2 p,vec2 h,float r,float sigma) {
            float lo=p.y-h.y,hi=p.y+h.y;
            float from=clamp(-3.0*sigma,lo,hi),to=clamp(3.0*sigma,lo,hi);
            float step=(to-from)/4.0,sum=0.0;
            for(int k=0;k<4;k++) {
                float y=from+step*(float(k)+0.5),dy=p.y-y;
                float delta=min(h.y-r-abs(dy),0.0);
                float curved=h.x-r+sqrt(max(0.0,r*r-delta*delta));
                vec2 v=0.5+0.5*erf2((p.x+vec2(-curved,curved))*(0.70710678/sigma));
                sum+=(v.y-v.x)*exp(-y*y/(2.0*sigma*sigma))/(2.5066283*sigma)*step;
            }
            return sum;
        }
        void main() {
            if(world.x<iclip.x||world.y<iclip.y||world.x>=iclip.z||world.y>=iclip.w) discard;
            float cut=1.0;
            if(iparams.w>0.0) {
                uint corners=uint(floor(iparams.w/4096.0));
                float rad=iparams.w-float(corners)*4096.0;
                vec2 h=(iclip.zw-iclip.xy)*0.5,q=world-iclip.xy-h;
                float d=rounded(q,h,min(rad,min(h.x,h.y)));
                float aa=max(fwidth(d),0.0001)*0.5;
                uint bit=q.y<0.0?(q.x<0.0?1u:2u):(q.x<0.0?8u:4u);
                if((corners&bit)!=0u) cut=1.0-smoothstep(-aa,aa,d);
            }
            float kind=iparams.z;
            if(kind==1.0) {
                float a=texture(atlas,texel/vec2(textureSize(atlas,0))).a*icolor.a*cut;
                outputColor=vec4(icolor.rgb*a,a); return;
            }
            vec2 h=irect.zw*0.5,p=local-h;
            float r=min(iparams.x,min(h.x,h.y));
            if(kind==2.0) {
                float a=shadow(p,h,r,max(iparams.y,0.25))*icolor.a*cut;
                outputColor=vec4(icolor.rgb*a,a); return;
            }
            float d=rounded(p,h,r),aa=max(fwidth(d),0.0001)*0.5;
            float coverage=(1.0-smoothstep(-aa,aa,d))*cut;
            if(kind==3.0) {
                outputColor=texture(atlas,texel/vec2(textureSize(atlas,0)))*(coverage*icolor.a); return;
            }
            vec4 c=icolor;
            if(iparams.y>0.0&&iborder.a>0.0)
                c=mix(c,iborder,smoothstep(-iparams.y-aa,-iparams.y+aa,d));
            float a=c.a*coverage;
            outputColor=vec4(c.rgb*a,a);
        }
        """;
}
