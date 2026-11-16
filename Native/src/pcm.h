// Engine-independent immutable PCM and deterministic renderer; MIT.
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>
namespace sf {
struct Pcm { std::vector<float> samples; int channels, rate; int64_t frames() const { return int64_t(samples.size())/channels; } };
inline std::shared_ptr<const Pcm> prepare(const float* samples,int64_t count,int channels,int rate) {
    if (!samples || channels<1 || channels>64 || rate<8000 || rate>384000 || count<channels || count>64*1024*1024 || count%channels) throw std::runtime_error("Invalid PCM dimensions/rate or 256 MiB sample limit");
    for (int64_t i=0;i<count;++i) if (!std::isfinite(samples[i])) throw std::runtime_error("PCM contains non-finite samples");
    return std::make_shared<const Pcm>(Pcm{std::vector<float>(samples,samples+count),channels,rate});
}
inline bool render(const Pcm& pcm,double& position,float* output,int frames,int channels,int rate,int selected,bool loop) {
    if (!output || frames<0 || frames>1024*1024 || channels<1 || channels>64 || rate<8000 || rate>384000 || selected<0 || selected>channels || !std::isfinite(position) || position<0) throw std::runtime_error("Invalid render configuration");
    double step=double(pcm.rate)/rate; int64_t total=pcm.frames();
    for (int f=0;f<frames;++f) {
        if (loop) position=std::fmod(position,double(total));
        bool valid=position<total; int64_t a=valid?int64_t(position):0, b=loop?(a+1)%total:std::min(a+1,total-1); float t=valid?float(position-a):0;
        for (int ch=0;ch<channels;++ch) {
            int source=selected==0?ch:(ch+1==selected?0:pcm.channels); float value=0;
            if (valid && source<pcm.channels) { float x=pcm.samples[size_t(a)*pcm.channels+source],y=pcm.samples[size_t(b)*pcm.channels+source]; value=x+(y-x)*t; }
            output[size_t(f)*channels+ch]=std::isfinite(value)?std::clamp(value,-1.f,1.f):0;
        }
        if (valid) position+=step;
    }
    return !loop && position>=total;
}
inline void encode(const float* source,int samples,uint8_t* output,bool floating,int bits,int valid) {
    if (!source || !output || samples<0 || (floating?bits!=32:(bits!=16 && bits!=24 && bits!=32)) || valid<1 || valid>bits) throw std::runtime_error("Unsupported device PCM format");
    for (int i=0;i<samples;++i) {
        float v=std::isfinite(source[i])?std::clamp(source[i],-1.f,1.f):0; uint32_t word;
        if (floating) std::memcpy(&word,&v,4);
        else { int64_t scale=int64_t(1)<<(valid-1); int64_t q=std::clamp<int64_t>(int64_t(std::round(double(v)*(scale-1))),-scale,scale-1); word=uint32_t(q)<<(bits-valid); }
        for (int b=0;b<bits/8;++b) *output++=uint8_t(word>>(8*b));
    }
}
// Inverse of encode(): little-endian device words to clamped floats. Throws on unsupported layouts.
inline void decode(const uint8_t* input,int samples,float* output,bool floating,int bits,int valid) {
    if (!input || !output || samples<0 || (floating?bits!=32:(bits!=16 && bits!=24 && bits!=32)) || valid<1 || valid>bits) throw std::runtime_error("Unsupported device PCM format");
    for (int i=0;i<samples;++i) {
        uint32_t word=0; for (int b=0;b<bits/8;++b) word|=uint32_t(*input++)<<(8*b);
        float v;
        if (floating) { std::memcpy(&v,&word,4); }
        else {
            int64_t q=bits==32?int64_t(int32_t(word)):(bits==24?int64_t(int32_t(word<<8)>>8):int64_t(int16_t(word)));
            q>>= (bits-valid); int64_t scale=int64_t(1)<<(valid-1); v=float(double(q)/double(scale));
        }
        output[i]=std::isfinite(v)?std::clamp(v,-1.f,1.f):0;
    }
}
}
