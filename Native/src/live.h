// Engine-independent live input -> output bridge: bounded ring, drift-servoed linear
// resampler and the same channel mapping rules as the PCM renderer. Offline testable; MIT.
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <mutex>
#include <stdexcept>
#include <vector>
namespace sf {
struct LiveStats { uint64_t captured=0, rendered=0, underruns=0, overruns=0; int fill_frames=0; float peak=0; bool primed=false; };
class LiveBridge {
public:
    // input_channel: 0 = all input channels, 1..N = that single input channel.
    // output_channel: 0 = corresponding channels (a single selected input lands on output 1),
    //                 1..N = the first selected source channel to exactly one output.
    LiveBridge(int in_channels,int in_rate,int out_channels,int out_rate,int input_channel,int output_channel,int target_ms)
        : ic(in_channels),ir(in_rate),oc(out_channels),orate(out_rate) {
        if (ic<1||ic>64||oc<1||oc>64||ir<8000||ir>384000||orate<8000||orate>384000||input_channel<0||input_channel>ic||output_channel<0||output_channel>oc||target_ms<5||target_ms>1000)
            throw std::runtime_error("Invalid live bridge configuration");
        target=std::max(16,int(std::llround(double(ir)*target_ms/1000.0)));capacity=target*8;ring.assign(size_t(capacity)*ic,0.f);
        const int first=input_channel?input_channel-1:0;map.assign(size_t(oc),-1);
        for (int o=0;o<oc;++o) {
            if (output_channel==0) map[o]=input_channel?(o==0?first:-1):(o<ic?o:-1);
            else map[o]=(o+1==output_channel)?first:-1;
        }
    }
    // Appends interleaved input. Never blocks on I/O; overflow drops the OLDEST audio and is counted.
    void push(const float* data,int frames) {
        if (!data||frames<0||frames>1024*1024) throw std::runtime_error("Invalid live input block");
        std::lock_guard<std::mutex> g(lock);
        if (frames>capacity) {data+=size_t(frames-capacity)*ic;frames=capacity;++stats.overruns;}
        const int free=capacity-count;
        if (frames>free) {const int drop=frames-free;head=advance(head,drop);count-=drop;readFraction=0;++stats.overruns;}
        for (int f=0;f<frames;++f) {
            const size_t at=size_t(advance(head,count+f))*ic;
            for (int c=0;c<ic;++c) {float v=data[size_t(f)*ic+c];v=std::isfinite(v)?std::clamp(v,-1.f,1.f):0.f;ring[at+c]=v;stats.peak=std::max(stats.peak,std::fabs(v));}
        }
        count+=frames;stats.captured+=uint64_t(frames);
    }
    // Fills `frames` interleaved output frames (silence where input is unavailable).
    // Returns the number of frames that were silence because of underrun after priming.
    int pull(float* out,int frames,float gain) {
        if (!out||frames<0||frames>1024*1024||!std::isfinite(gain)||gain<0||gain>4) throw std::runtime_error("Invalid live render request");
        std::lock_guard<std::mutex> g(lock);std::fill(out,out+size_t(frames)*oc,0.f);
        if (!stats.primed) {if (count<target) return 0;stats.primed=true;}
        if (count>target*4) {const int drop=count-target;head=advance(head,drop);count-=drop;readFraction=0;++stats.overruns;}
        for (int f=0;f<frames;++f) {
            if (count<2) {stats.primed=false;++stats.underruns;return frames-f;}
            // Proportional servo keeps the fill near target despite independent device clocks (+/-0.5%).
            const double error=(double(count)-target)/target;
            const double ratio=double(ir)/orate*(1.0+std::clamp(error*0.01,-0.005,0.005));
            const size_t a=size_t(head)*ic,b=size_t(advance(head,1))*ic;const float t=float(readFraction);
            for (int o=0;o<oc;++o) {
                const int src=map[size_t(o)];if (src<0) continue;
                const float x=ring[a+src],y=ring[b+src];out[size_t(f)*oc+o]=std::clamp((x+(y-x)*t)*gain,-1.f,1.f);
            }
            readFraction+=ratio;int whole=int(readFraction);
            if (whole>0) {whole=std::min(whole,count-1);head=advance(head,whole);count-=whole;readFraction-=whole;}
            ++stats.rendered;
        }
        return 0;
    }
    LiveStats snapshot() {std::lock_guard<std::mutex> g(lock);LiveStats s=stats;s.fill_frames=count;return s;}
    float take_peak() {std::lock_guard<std::mutex> g(lock);const float p=stats.peak;stats.peak=0;return p;}
    int target_frames() const {return target;}
private:
    int advance(int i,int n) const {return (i+n)%capacity;}
    int ic,ir,oc,orate,target=0,capacity=0,head=0,count=0;double readFraction=0;
    std::vector<int> map;std::vector<float> ring;LiveStats stats;std::mutex lock;
};
}
