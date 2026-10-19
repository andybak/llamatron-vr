#include "xr_session.h"
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iostream>
#include <stdexcept>

using namespace DirectX;
static void xrCheck(XrResult result,const char* name){if(XR_FAILED(result))throw std::runtime_error(std::string(name)+" failed (OpenXR "+std::to_string(result)+")");}
static HMODULE loader(){
    wchar_t executable[32768]{};GetModuleFileNameW(nullptr,executable,32768);
    const auto directory=std::filesystem::path(executable).parent_path()/"runtime";
    std::filesystem::create_directories(directory);const auto dll=directory/"openxr_loader.dll";
    HRSRC resource=FindResourceW(nullptr,MAKEINTRESOURCEW(101),RT_RCDATA);
    if(!resource)throw std::runtime_error("Embedded OpenXR loader is missing");
    const DWORD size=SizeofResource(nullptr,resource);const void* bytes=LockResource(LoadResource(nullptr,resource));
    // The loader is part of this executable; no separately installed SDK is needed.
    std::ofstream output(dll,std::ios::binary|std::ios::trunc);output.write(static_cast<const char*>(bytes),size);output.close();
    HMODULE module=LoadLibraryW(dll.c_str());if(!module)throw std::runtime_error("Cannot load the bundled OpenXR loader");return module;
}
NativeXR::NativeXR(){
    std::cout<<"[LLNATIVE] Loading embedded OpenXR loader\n";
    loader_=loader();getProc_=reinterpret_cast<PFN_xrGetInstanceProcAddr>(GetProcAddress(loader_,"xrGetInstanceProcAddr"));
    if(!getProc_)throw std::runtime_error("OpenXR loader entry is missing");
    PFN_xrCreateInstance create{};xrCheck(getProc_(XR_NULL_HANDLE,"xrCreateInstance",reinterpret_cast<PFN_xrVoidFunction*>(&create)),"Resolve xrCreateInstance");
    const char* extensions[]={XR_KHR_D3D11_ENABLE_EXTENSION_NAME};
    XrInstanceCreateInfo info{XR_TYPE_INSTANCE_CREATE_INFO};strcpy_s(info.applicationInfo.applicationName,"Llamatron Native");
    strcpy_s(info.applicationInfo.engineName,"Llamatron D3D11");info.applicationInfo.applicationVersion=1;
    info.applicationInfo.apiVersion=XR_MAKE_VERSION(1,0,0);info.enabledExtensionCount=1;info.enabledExtensionNames=extensions;
    std::cout<<"[LLNATIVE] Calling xrCreateInstance\n";
    xrCheck(create(&info,&instance_),"xrCreateInstance");
    std::cout<<"[LLNATIVE] xrCreateInstance completed\n";
    try{
#define BIND_XR(name) xrCheck(getProc_(instance_,#name,reinterpret_cast<PFN_xrVoidFunction*>(&name##_)),#name);
        LLAMA_XR_FUNCTIONS(BIND_XR)
#undef BIND_XR
        XrSystemGetInfo systemInfo{XR_TYPE_SYSTEM_GET_INFO};systemInfo.formFactor=XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY;
        xrCheck(xrGetSystem_(instance_,&systemInfo,&system_),"xrGetSystem");
        xrCheck(xrGetD3D11GraphicsRequirementsKHR_(instance_,system_,&requirements_),"xrGetD3D11GraphicsRequirementsKHR");
        std::cout<<"[LLNATIVE] OpenXR instance and HMD system ready\n";
    }catch(...){if(xrDestroyInstance_)xrDestroyInstance_(instance_);instance_=XR_NULL_HANDLE;FreeLibrary(loader_);loader_=nullptr;throw;}
}
NativeXR::~NativeXR(){
    for(const auto& swapchain:swapchains_)if(swapchain.handle)xrDestroySwapchain_(swapchain.handle);
    if(space_)xrDestroySpace_(space_);if(session_)xrDestroySession_(session_);if(actions_)xrDestroyActionSet_(actions_);
    if(instance_&&xrDestroyInstance_)xrDestroyInstance_(instance_);if(loader_)FreeLibrary(loader_);
}
XrPath NativeXR::path(const char* name){XrPath result{};xrCheck(xrStringToPath_(instance_,name,&result),"xrStringToPath");return result;}
void NativeXR::bindActions(){
    hands_={path("/user/hand/left"),path("/user/hand/right")};
    XrActionSetCreateInfo set{XR_TYPE_ACTION_SET_CREATE_INFO};strcpy_s(set.actionSetName,"llamatron");strcpy_s(set.localizedActionSetName,"Llamatron controls");
    xrCheck(xrCreateActionSet_(instance_,&set,&actions_),"xrCreateActionSet");
    auto action=[&](const char* name,const char* label,XrActionType type,XrAction& handle){
        XrActionCreateInfo info{XR_TYPE_ACTION_CREATE_INFO};strcpy_s(info.actionName,name);strcpy_s(info.localizedActionName,label);
        info.actionType=type;info.countSubactionPaths=2;info.subactionPaths=hands_.data();xrCheck(xrCreateAction_(actions_,&info,&handle),"xrCreateAction");
    };
    action("move","Move llama",XR_ACTION_TYPE_VECTOR2F_INPUT,movement_);
    action("fire_lock","Start and lock fire",XR_ACTION_TYPE_FLOAT_INPUT,trigger_);
    action("select","Start game",XR_ACTION_TYPE_BOOLEAN_INPUT,select_);
    for(const char* profile:{"/interaction_profiles/oculus/touch_controller","/interaction_profiles/valve/index_controller","/interaction_profiles/microsoft/motion_controller"}){
        const XrActionSuggestedBinding bindings[]={
            {movement_,path("/user/hand/left/input/thumbstick")},{movement_,path("/user/hand/right/input/thumbstick")},
            {trigger_,path("/user/hand/left/input/trigger/value")},{trigger_,path("/user/hand/right/input/trigger/value")}};
        XrInteractionProfileSuggestedBinding suggestion{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};
        suggestion.interactionProfile=path(profile);suggestion.countSuggestedBindings=4;suggestion.suggestedBindings=bindings;
        const auto result=xrSuggestInteractionProfileBindings_(instance_,&suggestion);
        if(XR_FAILED(result))std::cout<<"[LLNATIVE] Optional controller profile skipped: "<<profile<<" ("<<result<<")\n";
    }
    const XrActionSuggestedBinding simpleBindings[]={
        {select_,path("/user/hand/left/input/select/click")},{select_,path("/user/hand/right/input/select/click")}};
    XrInteractionProfileSuggestedBinding simple{XR_TYPE_INTERACTION_PROFILE_SUGGESTED_BINDING};simple.interactionProfile=path("/interaction_profiles/khr/simple_controller");
    simple.countSuggestedBindings=2;simple.suggestedBindings=simpleBindings;xrCheck(xrSuggestInteractionProfileBindings_(instance_,&simple),"Suggest simple controller bindings");
    XrSessionActionSetsAttachInfo attach{XR_TYPE_SESSION_ACTION_SETS_ATTACH_INFO};attach.countActionSets=1;attach.actionSets=&actions_;
    xrCheck(xrAttachSessionActionSets_(session_,&attach),"xrAttachSessionActionSets");
}
void NativeXR::attach(Renderer& renderer){
    if(renderer.device()->GetFeatureLevel()<requirements_.minFeatureLevel)throw std::runtime_error("D3D11 device does not meet OpenXR graphics requirements");
    XrGraphicsBindingD3D11KHR graphics{XR_TYPE_GRAPHICS_BINDING_D3D11_KHR};graphics.device=renderer.device();
    XrSessionCreateInfo create{XR_TYPE_SESSION_CREATE_INFO};create.next=&graphics;create.systemId=system_;
    xrCheck(xrCreateSession_(instance_,&create,&session_),"xrCreateSession");
    XrReferenceSpaceCreateInfo reference{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};reference.poseInReferenceSpace.orientation.w=1;reference.referenceSpaceType=XR_REFERENCE_SPACE_TYPE_STAGE;
    if(XR_FAILED(xrCreateReferenceSpace_(session_,&reference,&space_))){
        reference.referenceSpaceType=XR_REFERENCE_SPACE_TYPE_LOCAL;reference.poseInReferenceSpace.position.y=-1.6f;
        xrCheck(xrCreateReferenceSpace_(session_,&reference,&space_),"Create floor reference space");
    }
    bindActions();uint32_t count{};
    xrCheck(xrEnumerateViewConfigurationViews_(instance_,system_,XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,0,&count,nullptr),"Count OpenXR views");
    std::vector<XrViewConfigurationView> configurations(count,{XR_TYPE_VIEW_CONFIGURATION_VIEW});
    xrCheck(xrEnumerateViewConfigurationViews_(instance_,system_,XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,count,&count,configurations.data()),"Get OpenXR views");
    views_.resize(count,{XR_TYPE_VIEW});uint32_t formatCount{};xrCheck(xrEnumerateSwapchainFormats_(session_,0,&formatCount,nullptr),"Count OpenXR formats");
    std::vector<int64_t> formats(formatCount);xrCheck(xrEnumerateSwapchainFormats_(session_,formatCount,&formatCount,formats.data()),"Get OpenXR formats");
    const int64_t preferred[]={DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,DXGI_FORMAT_R8G8B8A8_UNORM,DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,DXGI_FORMAT_B8G8R8A8_UNORM};
    int64_t format=0;for(auto candidate:preferred)if(std::find(formats.begin(),formats.end(),candidate)!=formats.end()){format=candidate;break;}
    if(!format)throw std::runtime_error("OpenXR runtime has no supported D3D11 colour format");
    for(const auto& configuration:configurations){
        ViewSwapchain swapchain;swapchain.width=configuration.recommendedImageRectWidth;swapchain.height=configuration.recommendedImageRectHeight;
        XrSwapchainCreateInfo info{XR_TYPE_SWAPCHAIN_CREATE_INFO};info.usageFlags=XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT;info.format=format;
        info.sampleCount=1;info.width=swapchain.width;info.height=swapchain.height;info.faceCount=info.arraySize=info.mipCount=1;
        xrCheck(xrCreateSwapchain_(session_,&info,&swapchain.handle),"xrCreateSwapchain");uint32_t imageCount{};
        xrCheck(xrEnumerateSwapchainImages_(swapchain.handle,0,&imageCount,nullptr),"Count eye surfaces");
        swapchain.images.resize(imageCount,{XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
        xrCheck(xrEnumerateSwapchainImages_(swapchain.handle,imageCount,&imageCount,reinterpret_cast<XrSwapchainImageBaseHeader*>(swapchain.images.data())),"Get eye surfaces");
        // Runtime textures can be typeless. The view must use the negotiated colour format.
        D3D11_RENDER_TARGET_VIEW_DESC targetInfo{};targetInfo.Format=static_cast<DXGI_FORMAT>(format);
        targetInfo.ViewDimension=D3D11_RTV_DIMENSION_TEXTURE2D;
        swapchain.targets.resize(imageCount);
        for(uint32_t image=0;image<imageCount;image++){
            const HRESULT result=renderer.device()->CreateRenderTargetView(swapchain.images[image].texture,&targetInfo,&swapchain.targets[image]);
            if(FAILED(result)){
                D3D11_TEXTURE2D_DESC textureInfo{};swapchain.images[image].texture->GetDesc(&textureInfo);
                std::cerr<<"[LLNATIVE] Eye target creation failed: HRESULT 0x"<<std::hex<<static_cast<uint32_t>(result)<<std::dec
                    <<"; texture format "<<textureInfo.Format<<"; view format "<<format<<"\n";
                throw std::runtime_error("Cannot create OpenXR eye render target");
            }
        }
        swapchains_.push_back(std::move(swapchain));
    }
    std::cout<<"[LLNATIVE] Native stereo session created; both hand subaction paths attached\n";
}
void NativeXR::events(){
    XrEventDataBuffer event{XR_TYPE_EVENT_DATA_BUFFER};
    while(xrPollEvent_(instance_,&event)==XR_SUCCESS){
        if(event.type==XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED){
            const auto& change=*reinterpret_cast<XrEventDataSessionStateChanged*>(&event);focused_=change.state==XR_SESSION_STATE_FOCUSED;
            std::cout<<"[LLNATIVE] OpenXR session state "<<change.state<<"\n";
            if(change.state==XR_SESSION_STATE_READY){XrSessionBeginInfo begin{XR_TYPE_SESSION_BEGIN_INFO};begin.primaryViewConfigurationType=XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
                xrCheck(xrBeginSession_(session_,&begin),"xrBeginSession");running_=true;}
            if(change.state==XR_SESSION_STATE_STOPPING){running_=false;xrCheck(xrEndSession_(session_),"xrEndSession");}
            if(change.state==XR_SESSION_STATE_EXITING||change.state==XR_SESSION_STATE_LOSS_PENDING)quit_=true;
        }
        if(event.type==XR_TYPE_EVENT_DATA_INSTANCE_LOSS_PENDING)quit_=true;
        event={XR_TYPE_EVENT_DATA_BUFFER};
    }
}
uint8_t NativeXR::controls(){
    if(!focused_)return 0;
    const XrActiveActionSet active{actions_,XR_NULL_PATH};XrActionsSyncInfo sync{XR_TYPE_ACTIONS_SYNC_INFO};sync.countActiveActionSets=1;sync.activeActionSets=&active;
    const auto result=xrSyncActions_(session_,&sync);if(result==XR_SESSION_NOT_FOCUSED)return 0;xrCheck(result,"xrSyncActions");
    XrVector2f movement{};bool leftActive=false,fire=false;
    for(size_t hand=0;hand<hands_.size();hand++){
        XrActionStateGetInfo get{XR_TYPE_ACTION_STATE_GET_INFO};get.subactionPath=hands_[hand];get.action=movement_;
        XrActionStateVector2f stick{XR_TYPE_ACTION_STATE_VECTOR2F};xrCheck(xrGetActionStateVector2f_(session_,&get,&stick),"Read native thumbstick");
        const bool moving=stick.isActive&&(std::abs(stick.currentState.x)>.25f||std::abs(stick.currentState.y)>.25f);
        if(moving&&(hand==0||!leftActive)){movement=stick.currentState;if(hand==0)leftActive=true;}
        get.action=trigger_;XrActionStateFloat trigger{XR_TYPE_ACTION_STATE_FLOAT};xrCheck(xrGetActionStateFloat_(session_,&get,&trigger),"Read native trigger");
        fire|=trigger.isActive&&trigger.currentState>.5f;
        get.action=select_;XrActionStateBoolean select{XR_TYPE_ACTION_STATE_BOOLEAN};xrCheck(xrGetActionStateBoolean_(session_,&get,&select),"Read native select");
        fire|=select.isActive&&select.currentState;
    }
    return (movement.x<-.25f?4:0)|(movement.x>.25f?8:0)|(movement.y>.25f?1:0)|(movement.y<-.25f?2:0)|(fire?128:0);
}
bool NativeXR::frame(Renderer& renderer,const SceneFrame& scene,uint8_t& input){
    events();input=0;if(!running_)return false;
    XrFrameWaitInfo wait{XR_TYPE_FRAME_WAIT_INFO};XrFrameState state{XR_TYPE_FRAME_STATE};xrCheck(xrWaitFrame_(session_,&wait,&state),"xrWaitFrame");
    XrFrameBeginInfo begin{XR_TYPE_FRAME_BEGIN_INFO};xrCheck(xrBeginFrame_(session_,&begin),"xrBeginFrame");input=controls();
    std::vector<XrCompositionLayerProjectionView> projections;XrCompositionLayerProjection layer{XR_TYPE_COMPOSITION_LAYER_PROJECTION};layer.space=space_;
    if(state.shouldRender){
        XrViewLocateInfo locate{XR_TYPE_VIEW_LOCATE_INFO};locate.viewConfigurationType=XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;locate.displayTime=state.predictedDisplayTime;locate.space=space_;
        XrViewState viewState{XR_TYPE_VIEW_STATE};uint32_t count{};xrCheck(xrLocateViews_(session_,&locate,&viewState,static_cast<uint32_t>(views_.size()),&count,views_.data()),"xrLocateViews");
        if((viewState.viewStateFlags&XR_VIEW_STATE_ORIENTATION_VALID_BIT)&&(viewState.viewStateFlags&XR_VIEW_STATE_POSITION_VALID_BIT))for(uint32_t i=0;i<count;i++){
            auto& swapchain=swapchains_[i];uint32_t index{};XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
            xrCheck(xrAcquireSwapchainImage_(swapchain.handle,&acquire,&index),"Acquire eye image");
            XrSwapchainImageWaitInfo ready{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO};ready.timeout=XR_INFINITE_DURATION;xrCheck(xrWaitSwapchainImage_(swapchain.handle,&ready),"Wait eye image");
            const auto& pose=views_[i].pose;const auto& q=pose.orientation;const auto& p=pose.position;
            const XMMATRIX view=XMMatrixInverse(nullptr,XMMatrixRotationQuaternion(XMVectorSet(q.x,q.y,q.z,q.w))*XMMatrixTranslation(p.x,p.y,p.z));
            const auto& fov=views_[i].fov;const float nearPlane=.02f;
            const XMMATRIX projection=XMMatrixPerspectiveOffCenterRH(std::tan(fov.angleLeft)*nearPlane,std::tan(fov.angleRight)*nearPlane,
                std::tan(fov.angleDown)*nearPlane,std::tan(fov.angleUp)*nearPlane,nearPlane,50);
            renderer.render(scene,swapchain.targets[index].Get(),swapchain.width,swapchain.height,view,projection);
            XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};xrCheck(xrReleaseSwapchainImage_(swapchain.handle,&release),"Release eye image");
            XrCompositionLayerProjectionView projectionView{XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW};projectionView.pose=pose;projectionView.fov=fov;
            projectionView.subImage.swapchain=swapchain.handle;projectionView.subImage.imageRect.extent={static_cast<int32_t>(swapchain.width),static_cast<int32_t>(swapchain.height)};
            projections.push_back(projectionView);
        }
    }
    layer.viewCount=static_cast<uint32_t>(projections.size());layer.views=projections.data();const XrCompositionLayerBaseHeader* layers[]={reinterpret_cast<const XrCompositionLayerBaseHeader*>(&layer)};
    XrFrameEndInfo end{XR_TYPE_FRAME_END_INFO};end.displayTime=state.predictedDisplayTime;end.environmentBlendMode=XR_ENVIRONMENT_BLEND_MODE_OPAQUE;
    end.layerCount=projections.empty()?0:1;end.layers=layers;xrCheck(xrEndFrame_(session_,&end),"xrEndFrame");return true;
}
