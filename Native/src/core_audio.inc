// macOS Core Audio implementation, included inside the backend's private namespace.
// No Unity mixer, managed callbacks, or silent configured-device fallback. MIT.
struct Cancelled {};
void ca_check(OSStatus result,const char* operation) {if(result!=noErr)throw std::runtime_error(std::string(operation)+" (Core Audio status "+std::to_string(result)+")");}
AudioObjectPropertyAddress ca_address(AudioObjectPropertySelector selector,AudioObjectPropertyScope scope=kAudioObjectPropertyScopeGlobal) {return {selector,scope,kAudioObjectPropertyElementMain};}
template<class T> T ca_value(AudioObjectID object,AudioObjectPropertySelector selector,AudioObjectPropertyScope scope=kAudioObjectPropertyScopeGlobal) {
    auto address=ca_address(selector,scope);T value{};UInt32 bytes=sizeof(value);ca_check(AudioObjectGetPropertyData(object,&address,0,nullptr,&bytes,&value),"Read Core Audio property");return value;
}
std::string ca_string(AudioObjectID device,AudioObjectPropertySelector selector) {
    CFStringRef value=ca_value<CFStringRef>(device,selector);if(!value)return {};
    CFIndex length=CFStringGetMaximumSizeForEncoding(CFStringGetLength(value),kCFStringEncodingUTF8)+1;
    if(length<1 || length>32768){CFRelease(value);throw std::runtime_error("Core Audio string size limit");}
    std::string result(size_t(length),'\0');bool ok=CFStringGetCString(value,result.data(),length,kCFStringEncodingUTF8);CFRelease(value);
    if(!ok)throw std::runtime_error("Invalid Core Audio UTF-8");result.resize(std::strlen(result.c_str()));return result;
}
std::vector<AudioDeviceID> ca_devices() {
    auto address=ca_address(kAudioHardwarePropertyDevices);UInt32 bytes=0;ca_check(AudioObjectGetPropertyDataSize(kAudioObjectSystemObject,&address,0,nullptr,&bytes),"Count Core Audio devices");
    if(bytes>256*sizeof(AudioDeviceID) || bytes%sizeof(AudioDeviceID))throw std::runtime_error("Core Audio device count limit");
    std::vector<AudioDeviceID> result(bytes/sizeof(AudioDeviceID));if(bytes)ca_check(AudioObjectGetPropertyData(kAudioObjectSystemObject,&address,0,nullptr,&bytes,result.data()),"Enumerate Core Audio devices");if(bytes%sizeof(AudioDeviceID) || bytes>result.size()*sizeof(AudioDeviceID))throw std::runtime_error("Core Audio device list changed");result.resize(bytes/sizeof(AudioDeviceID));return result;
}
int ca_channels(AudioDeviceID device,AudioObjectPropertyScope scope=kAudioDevicePropertyScopeOutput) {
    auto address=ca_address(kAudioDevicePropertyStreamConfiguration,scope);UInt32 bytes=0;
    ca_check(AudioObjectGetPropertyDataSize(device,&address,0,nullptr,&bytes),"Output stream configuration size");
    if(bytes<offsetof(AudioBufferList,mBuffers) || bytes>65536)throw std::runtime_error("Invalid Core Audio stream configuration");
    std::vector<uint8_t> storage(bytes);ca_check(AudioObjectGetPropertyData(device,&address,0,nullptr,&bytes,storage.data()),"Output stream configuration");auto list=reinterpret_cast<AudioBufferList*>(storage.data());
    if(offsetof(AudioBufferList,mBuffers)+size_t(list->mNumberBuffers)*sizeof(AudioBuffer)>bytes)throw std::runtime_error("Truncated Core Audio buffers");
    int channels=0;for(UInt32 i=0;i<list->mNumberBuffers;++i){if(list->mBuffers[i].mNumberChannels>64)throw std::runtime_error("Core Audio channel limit");channels+=int(list->mBuffers[i].mNumberChannels);}if(channels>64)throw std::runtime_error("Core Audio channel limit");return channels;
}
std::string devices(bool capture=false) {
    const auto scope=capture?kAudioDevicePropertyScopeInput:kAudioDevicePropertyScopeOutput;
    AudioDeviceID def=ca_value<AudioDeviceID>(kAudioObjectSystemObject,capture?kAudioHardwarePropertyDefaultInputDevice:kAudioHardwarePropertyDefaultOutputDevice);std::string result="{\"devices\":[";bool first=true;
    for(auto device:ca_devices()) {
        int channels=ca_channels(device,scope);if(!channels)continue;double rate=ca_value<Float64>(device,kAudioDevicePropertyNominalSampleRate);
        if(!std::isfinite(rate) || rate<8000 || rate>384000)continue;
        if(!first)result+=',';first=false;result+="{\"id\":"+json_string(ca_string(device,kAudioDevicePropertyDeviceUID))+",\"name\":"+json_string(ca_string(device,kAudioObjectPropertyName))+",\"channels\":"+std::to_string(channels)+",\"sample_rate\":"+std::to_string(int(rate))+",\"is_default\":"+(device==def?"true":"false")+"}";
    }return result+"]}";
}
struct CaHog {
    AudioDeviceID device;bool acquired=false;
    CaHog(AudioDeviceID id,bool requested):device(id) {
        if(!requested)return;auto address=ca_address(kAudioDevicePropertyHogMode);pid_t owner=ca_value<pid_t>(id,kAudioDevicePropertyHogMode);
        if(owner!=-1)throw std::runtime_error("Exclusive Core Audio device already owned; no shared fallback");
        pid_t me=getpid();ca_check(AudioObjectSetPropertyData(id,&address,0,nullptr,sizeof(me),&me),"Acquire exclusive Core Audio hog mode");
        acquired=true;try{if(ca_value<pid_t>(id,kAudioDevicePropertyHogMode)!=me)throw std::runtime_error("Exclusive Core Audio ownership refused");}catch(...){release();throw;}
    }
    void release() noexcept {if(acquired){auto a=ca_address(kAudioDevicePropertyHogMode);pid_t owner=-1;UInt32 n=sizeof(owner);if(AudioObjectGetPropertyData(device,&a,0,nullptr,&n,&owner)==noErr && owner==getpid()){owner=-1;AudioObjectSetPropertyData(device,&a,0,nullptr,sizeof(owner),&owner);}acquired=false;}}
    ~CaHog(){release();}
};
struct CaQueue { AudioQueueRef value=nullptr;~CaQueue(){if(value){AudioQueueStop(value,true);AudioQueueDispose(value,true);}} };
struct CaRender {
    sf_player* player;std::shared_ptr<const sf::Pcm> pcm;int channels,rate,selected,frames;double position=0;
    std::atomic<uint64_t> returned{0};
    std::atomic<int> pending{0};std::atomic<bool> eof{false},failed{false};std::atomic<OSStatus> error{noErr};
    bool fill(AudioQueueRef queue,AudioQueueBufferRef buffer) noexcept {
        try {
            if(player->stop)return false;
            if(eof && !player->loop)return false;
            bool ended=sf::render(*pcm,position,static_cast<float*>(buffer->mAudioData),frames,channels,rate,selected,player->loop.load());eof=ended;
            buffer->mAudioDataByteSize=UInt32(frames*channels*sizeof(float));++pending;
            OSStatus result=AudioQueueEnqueueBuffer(queue,buffer,0,nullptr);if(result!=noErr){--pending;error=result;failed=true;return false;}
            {std::lock_guard<std::mutex> guard(player->status_lock);player->status.rendered_frames+=frames;}
            return true;
        }catch(...){failed=true;return false;}
    }
    static void callback(void* context,AudioQueueRef queue,AudioQueueBufferRef buffer) noexcept {auto self=static_cast<CaRender*>(context);self->fill(queue,buffer);--self->pending;++self->returned;}
};
int run_device(sf_player* p,std::shared_ptr<const sf::Pcm> pcm,const std::string& id,int selected,bool exclusive,int buffer_ms,bool& started) {
    AudioDeviceID device=kAudioObjectUnknown;
    if(id.empty())device=ca_value<AudioDeviceID>(kAudioObjectSystemObject,kAudioHardwarePropertyDefaultOutputDevice);
    else for(auto candidate:ca_devices())if(ca_string(candidate,kAudioDevicePropertyDeviceUID)==id){device=candidate;break;}
    if(device==kAudioObjectUnknown)throw std::runtime_error("Configured Core Audio output missing (no default fallback)");
    int channels=ca_channels(device);double nominal=ca_value<Float64>(device,kAudioDevicePropertyNominalSampleRate);
    if(channels<1 || channels>64 || selected<0 || selected>channels || !std::isfinite(nominal) || nominal<8000 || nominal>384000)throw std::runtime_error("Invalid Core Audio output channels/rate");
    int rate=int(std::llround(nominal));CaHog hog(device,exclusive);
    CaRender render{p,std::move(pcm),channels,rate,selected,std::max(32,int(std::llround(rate*(buffer_ms?buffer_ms/3000.0:.01))))};
    CaQueue queue;AudioStreamBasicDescription format{};format.mSampleRate=rate;format.mFormatID=kAudioFormatLinearPCM;format.mFormatFlags=kAudioFormatFlagIsFloat|kAudioFormatFlagsNativeEndian|kAudioFormatFlagIsPacked;
    format.mBytesPerPacket=format.mBytesPerFrame=UInt32(channels*sizeof(float));format.mFramesPerPacket=1;format.mChannelsPerFrame=UInt32(channels);format.mBitsPerChannel=32;
    ca_check(AudioQueueNewOutput(&format,&CaRender::callback,&render,nullptr,nullptr,0,&queue.value),"Create Core Audio output queue");
    auto uid=ca_string(device,kAudioDevicePropertyDeviceUID);CFStringRef target=CFStringCreateWithBytes(nullptr,reinterpret_cast<const UInt8*>(uid.data()),CFIndex(uid.size()),kCFStringEncodingUTF8,false);
    if(!target)throw std::runtime_error("Create Core Audio output UID failed");OSStatus route=AudioQueueSetProperty(queue.value,kAudioQueueProperty_CurrentDevice,&target,sizeof(target));CFRelease(target);ca_check(route,"Select exact Core Audio output");
    AudioChannelLayout layout{};layout.mChannelLayoutTag=kAudioChannelLayoutTag_DiscreteInOrder|UInt32(channels);ca_check(AudioQueueSetProperty(queue.value,kAudioQueueProperty_ChannelLayout,&layout,offsetof(AudioChannelLayout,mChannelDescriptions)),"Set discrete output channel order");
    for(int i=0;i<3;++i){AudioQueueBufferRef buffer=nullptr;ca_check(AudioQueueAllocateBuffer(queue.value,UInt32(render.frames*channels*sizeof(float)),&buffer),"Allocate Core Audio buffer");if(!render.fill(queue.value,buffer))break;}
    if(p->stop)throw Cancelled{};if(render.failed)throw std::runtime_error("Core Audio initial PCM submission failed");
    ca_check(AudioQueueStart(queue.value,nullptr),"Start Core Audio output");started=true;
    {std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_PLAYING;++p->status.started_sequence;p->status.output_channels=channels;p->status.output_rate=rate;p->status.exclusive_active=exclusive?1:0;p->status.buffer_frames=render.frames*3;}
    auto progress=std::chrono::steady_clock::now();uint64_t lastReturned=render.returned;
    while(render.pending>0){if(p->stop)throw Cancelled{};if(render.failed)throw std::runtime_error("Core Audio callback submission failed: "+std::to_string(render.error.load()));if(!ca_value<UInt32>(device,kAudioDevicePropertyDeviceIsAlive))throw std::runtime_error("Core Audio output device lost");
        if(render.eof){std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_DRAINING;}
        uint64_t returned=render.returned;if(returned!=lastReturned){lastReturned=returned;progress=std::chrono::steady_clock::now();}
        if(std::chrono::steady_clock::now()-progress>std::chrono::seconds(8))throw std::runtime_error("Core Audio drain timed out");std::this_thread::sleep_for(std::chrono::milliseconds(5));}
    if(render.failed)throw std::runtime_error("Core Audio callback failed");
    ca_check(AudioQueueStop(queue.value,false),"Drain Core Audio queue");auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(8);
    for(;;){if(p->stop)throw Cancelled{};UInt32 running=0,bytes=sizeof(running);ca_check(AudioQueueGetProperty(queue.value,kAudioQueueProperty_IsRunning,&running,&bytes),"Observe Core Audio drain");if(!running)break;if(std::chrono::steady_clock::now()>deadline)throw std::runtime_error("Core Audio stop timed out");std::this_thread::sleep_for(std::chrono::milliseconds(5));}
    return SF_NATURAL_END;
}
// ---- Live input (capture queue -> bridge -> render queue). Unverified on real Macs. ----
AudioDeviceID ca_resolve(const std::string& id,bool capture) {
    if(id.empty())return ca_value<AudioDeviceID>(kAudioObjectSystemObject,capture?kAudioHardwarePropertyDefaultInputDevice:kAudioHardwarePropertyDefaultOutputDevice);
    for(auto candidate:ca_devices())if(ca_string(candidate,kAudioDevicePropertyDeviceUID)==id)return candidate;
    return kAudioObjectUnknown;
}
void ca_route(AudioQueueRef queue,AudioDeviceID device,const char* what) {
    auto uid=ca_string(device,kAudioDevicePropertyDeviceUID);CFStringRef target=CFStringCreateWithBytes(nullptr,reinterpret_cast<const UInt8*>(uid.data()),CFIndex(uid.size()),kCFStringEncodingUTF8,false);
    if(!target)throw std::runtime_error(std::string("Create Core Audio UID failed: ")+what);OSStatus route=AudioQueueSetProperty(queue,kAudioQueueProperty_CurrentDevice,&target,sizeof(target));CFRelease(target);ca_check(route,what);
}
AudioStreamBasicDescription ca_float_format(int rate,int channels) {
    AudioStreamBasicDescription format{};format.mSampleRate=rate;format.mFormatID=kAudioFormatLinearPCM;format.mFormatFlags=kAudioFormatFlagIsFloat|kAudioFormatFlagsNativeEndian|kAudioFormatFlagIsPacked;
    format.mBytesPerPacket=format.mBytesPerFrame=UInt32(channels*sizeof(float));format.mFramesPerPacket=1;format.mChannelsPerFrame=UInt32(channels);format.mBitsPerChannel=32;return format;
}
struct CaLive {
    sf_live* live;sf::LiveBridge* bridge;int in_ch,out_ch,out_frames;std::atomic<bool> failed{false};
    static void in_callback(void* context,AudioQueueRef queue,AudioQueueBufferRef buffer,const AudioTimeStamp*,UInt32,const AudioStreamPacketDescription*) noexcept {
        auto self=static_cast<CaLive*>(context);if(self->live->stop)return;
        try {int frames=int(buffer->mAudioDataByteSize/(self->in_ch*sizeof(float)));if(frames>0)self->bridge->push(static_cast<float*>(buffer->mAudioData),frames);}catch(...) {self->failed=true;return;}
        if(AudioQueueEnqueueBuffer(queue,buffer,0,nullptr)!=noErr)self->failed=true;
    }
    bool fill_out(AudioQueueRef queue,AudioQueueBufferRef buffer) noexcept {
        try {bridge->pull(static_cast<float*>(buffer->mAudioData),out_frames,live->gain.load());buffer->mAudioDataByteSize=UInt32(out_frames*out_ch*sizeof(float));}catch(...) {failed=true;return false;}
        if(AudioQueueEnqueueBuffer(queue,buffer,0,nullptr)!=noErr) {failed=true;return false;}return true;
    }
    static void out_callback(void* context,AudioQueueRef queue,AudioQueueBufferRef buffer) noexcept {auto self=static_cast<CaLive*>(context);if(!self->live->stop)self->fill_out(queue,buffer);}
};
int run_live(sf_live* p,const std::string& in_id,int in_sel,const std::string& out_id,int out_sel,bool exclusive,int buffer_ms,bool& started) {
    AudioDeviceID in=ca_resolve(in_id,true),out=ca_resolve(out_id,false);
    if(in==kAudioObjectUnknown)throw std::runtime_error("Configured Core Audio input missing (no default fallback)");
    if(out==kAudioObjectUnknown)throw std::runtime_error("Configured Core Audio output missing (no default fallback)");
    int in_ch=ca_channels(in,kAudioDevicePropertyScopeInput),out_ch=ca_channels(out);double in_nominal=ca_value<Float64>(in,kAudioDevicePropertyNominalSampleRate),out_nominal=ca_value<Float64>(out,kAudioDevicePropertyNominalSampleRate);
    if(in_ch<1 || in_ch>64 || out_ch<1 || out_ch>64 || in_sel<0 || in_sel>in_ch || out_sel<0 || out_sel>out_ch || !std::isfinite(in_nominal) || !std::isfinite(out_nominal) || in_nominal<8000 || in_nominal>384000 || out_nominal<8000 || out_nominal>384000)throw std::runtime_error("Invalid Core Audio live channels/rates");
    int in_rate=int(std::llround(in_nominal)),out_rate=int(std::llround(out_nominal));CaHog hog(out,exclusive);
    int out_frames=std::max(32,int(std::llround(out_rate*(buffer_ms?buffer_ms/3000.0:.01)))),in_frames=std::max(32,int(std::llround(in_rate*(buffer_ms?buffer_ms/3000.0:.01))));
    sf::LiveBridge bridge(in_ch,in_rate,out_ch,out_rate,in_sel,out_sel,std::clamp(buffer_ms>0?buffer_ms*2:40,20,500));
    CaLive state{p,&bridge,in_ch,out_ch,out_frames};CaQueue in_queue,out_queue;
    auto in_format=ca_float_format(in_rate,in_ch);ca_check(AudioQueueNewInput(&in_format,&CaLive::in_callback,&state,nullptr,nullptr,0,&in_queue.value),"Create Core Audio input queue (microphone permission may be required)");
    ca_route(in_queue.value,in,"Select exact Core Audio input");
    for(int i=0;i<3;++i){AudioQueueBufferRef buffer=nullptr;ca_check(AudioQueueAllocateBuffer(in_queue.value,UInt32(in_frames*in_ch*sizeof(float)),&buffer),"Allocate Core Audio input buffer");ca_check(AudioQueueEnqueueBuffer(in_queue.value,buffer,0,nullptr),"Enqueue Core Audio input buffer");}
    auto out_format=ca_float_format(out_rate,out_ch);ca_check(AudioQueueNewOutput(&out_format,&CaLive::out_callback,&state,nullptr,nullptr,0,&out_queue.value),"Create Core Audio output queue");
    ca_route(out_queue.value,out,"Select exact Core Audio output");
    AudioChannelLayout layout{};layout.mChannelLayoutTag=kAudioChannelLayoutTag_DiscreteInOrder|UInt32(out_ch);ca_check(AudioQueueSetProperty(out_queue.value,kAudioQueueProperty_ChannelLayout,&layout,offsetof(AudioChannelLayout,mChannelDescriptions)),"Set discrete output channel order");
    for(int i=0;i<3;++i){AudioQueueBufferRef buffer=nullptr;ca_check(AudioQueueAllocateBuffer(out_queue.value,UInt32(out_frames*out_ch*sizeof(float)),&buffer),"Allocate Core Audio output buffer");if(!state.fill_out(out_queue.value,buffer))break;}
    if(p->stop)throw Cancelled{};if(state.failed)throw std::runtime_error("Core Audio initial live submission failed");
    ca_check(AudioQueueStart(in_queue.value,nullptr),"Start Core Audio input");ca_check(AudioQueueStart(out_queue.value,nullptr),"Start Core Audio output");started=true;
    {std::lock_guard<std::mutex> lock(p->status_lock);p->status.state=SF_PLAYING;++p->status.started_sequence;p->status.input_channels=in_ch;p->status.input_rate=in_rate;p->status.output_channels=out_ch;p->status.output_rate=out_rate;p->status.exclusive_active=exclusive?1:0;p->status.buffer_frames=out_frames*3;}
    auto last_input=std::chrono::steady_clock::now();uint64_t last_captured=0;
    for(;;) {
        if(p->stop)throw Cancelled{};
        if(state.failed)throw std::runtime_error("Core Audio live callback failed");
        if(!ca_value<UInt32>(in,kAudioDevicePropertyDeviceIsAlive) || !ca_value<UInt32>(out,kAudioDevicePropertyDeviceIsAlive))throw std::runtime_error("Core Audio live device lost");
        auto stats=bridge.snapshot();float peak=bridge.take_peak();
        if(stats.captured!=last_captured){last_captured=stats.captured;last_input=std::chrono::steady_clock::now();}
        else if(std::chrono::steady_clock::now()-last_input>std::chrono::seconds(3))throw std::runtime_error("No Core Audio input data for 3 seconds; device unavailable or microphone access denied");
        {std::lock_guard<std::mutex> lock(p->status_lock);p->status.captured_frames=stats.captured;p->status.rendered_frames=stats.rendered;p->status.underruns=stats.underruns;p->status.overruns=stats.overruns;p->status.latency_frames=stats.fill_frames;p->status.input_peak=std::max(p->status.input_peak,peak);}
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
}
