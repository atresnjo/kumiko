// Private synchronization fixture. No ALSA library/device is loaded.
#include <pthread.h>
#include <stdint.h>
#include <stdatomic.h>
static pthread_mutex_t mutex = PTHREAD_MUTEX_INITIALIZER;
static pthread_cond_t changed = PTHREAD_COND_INITIALIZER;
static int entered, stopped, result, prepared;
static int hold_write, write_entered, write_exited, write_release, write_result;
static atomic_int early_reuse, valid_samples;
void fixture_init(int value)
{
    entered = 0;
    stopped = 0;
    prepared = 0;
    result = value;
    hold_write = 0;
    write_entered = 0;
    write_exited = 0;
    write_release = 0;
    early_reuse = 0;
    valid_samples = 1;
}
void fixture_wait_drain(void)
{
    pthread_mutex_lock(&mutex);
    while (!entered)
        pthread_cond_wait(&changed, &mutex);
    pthread_mutex_unlock(&mutex);
}
int fixture_prepared(void) { return prepared; }
int snd_pcm_open(void **handle, const char *name, int stream, int mode)
{
    *handle = (void*)1;
    return 0;
}
int snd_pcm_set_params(void *handle, int format, int access, unsigned channels, unsigned rate, int resample, unsigned latency) { return 0; }
void fixture_hold_write(int value)
{
    hold_write = 1;
    write_result = value;
}
void fixture_wait_write(void)
{
    pthread_mutex_lock(&mutex);
    while (!write_entered)
        pthread_cond_wait(&changed, &mutex);
    pthread_mutex_unlock(&mutex);
}
void fixture_wait_drop(void)
{
    pthread_mutex_lock(&mutex);
    while (!stopped)
        pthread_cond_wait(&changed, &mutex);
    pthread_mutex_unlock(&mutex);
}
void fixture_release_write(void)
{
    pthread_mutex_lock(&mutex);
    write_release = 1;
    pthread_cond_broadcast(&changed);
    pthread_mutex_unlock(&mutex);
}
int fixture_early_reuse(void) { return early_reuse; }
int fixture_valid_samples(void) { return valid_samples; }
long snd_pcm_writei(void *handle, void *buffer, unsigned long frames)
{
    if (!hold_write)
        return frames;
    pthread_mutex_lock(&mutex);
    write_entered = 1;
    pthread_cond_broadcast(&changed);
    while (!stopped || !write_release)
        pthread_cond_wait(&changed, &mutex);
    float *samples = buffer;
    valid_samples = frames > 0 && samples[0] == .5f && samples[frames - 1] == .5f;
    write_exited = 1;
    pthread_mutex_unlock(&mutex);
    return write_result > 0 ? (long)frames : write_result;
}
int snd_pcm_recover(void *handle, int error, int silent)
{
    early_reuse++;
    return 0;
}
int snd_pcm_drain(void *handle)
{
    pthread_mutex_lock(&mutex);
    entered = 1;
    pthread_cond_broadcast(&changed);
    while (!stopped)
        pthread_cond_wait(&changed, &mutex);
    pthread_mutex_unlock(&mutex);
    return result;
}
int snd_pcm_drop(void *handle)
{
    pthread_mutex_lock(&mutex);
    stopped = 1;
    pthread_cond_broadcast(&changed);
    pthread_mutex_unlock(&mutex);
    return 0;
}
int snd_pcm_prepare(void *handle)
{
    if (write_entered && !write_exited)
        early_reuse++;
    prepared++;
    return 0;
}
int snd_pcm_close(void *handle)
{
    if (write_entered && !write_exited)
        early_reuse++;
    return 0;
}
const char *snd_strerror(int code) { return "private interrupted drain fixture"; }
