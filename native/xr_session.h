#pragma once
#include "renderer.h"
#include <openxr/openxr.h>
#include <openxr/openxr_platform.h>
#include <memory>

#define LLAMA_XR_FUNCTIONS(X) \
 X(xrDestroyInstance) X(xrGetSystem) X(xrGetD3D11GraphicsRequirementsKHR) \
 X(xrCreateSession) X(xrCreateReferenceSpace) X(xrCreateActionSet) X(xrCreateAction) \
 X(xrStringToPath) X(xrSuggestInteractionProfileBindings) X(xrAttachSessionActionSets) \
 X(xrGetActionStateVector2f) X(xrGetActionStateFloat) X(xrGetActionStateBoolean) X(xrSyncActions) \
 X(xrEnumerateViewConfigurationViews) X(xrEnumerateSwapchainFormats) X(xrCreateSwapchain) \
 X(xrEnumerateSwapchainImages) X(xrPollEvent) X(xrBeginSession) X(xrEndSession) \
 X(xrWaitFrame) X(xrBeginFrame) X(xrLocateViews) X(xrAcquireSwapchainImage) \
 X(xrWaitSwapchainImage) X(xrReleaseSwapchainImage) X(xrEndFrame) \
 X(xrDestroySwapchain) X(xrDestroySpace) X(xrDestroyActionSet) X(xrDestroySession)

class NativeXR {
public:
    NativeXR();
    ~NativeXR();
    LUID adapter() const {return requirements_.adapterLuid;}
    void attach(Renderer& renderer);
    void events();
    bool frame(Renderer& renderer,const SceneFrame& scene,uint8_t& input);
    bool quit() const {return quit_;}
    bool focused() const {return focused_;}
private:
    void bindActions();
    XrPath path(const char* name);
    uint8_t controls();
    struct ViewSwapchain {XrSwapchain handle{};uint32_t width{},height{};std::vector<XrSwapchainImageD3D11KHR> images;std::vector<Microsoft::WRL::ComPtr<ID3D11RenderTargetView>> targets;};
    HMODULE loader_{};
    PFN_xrGetInstanceProcAddr getProc_{};
    XrInstance instance_{};
    XrSystemId system_{};
    XrSession session_{};
    XrSpace space_{};
    XrActionSet actions_{};
    XrAction movement_{},trigger_{},select_{};
    std::array<XrPath,2> hands_{};
    XrGraphicsRequirementsD3D11KHR requirements_{XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR};
    std::vector<ViewSwapchain> swapchains_;
    std::vector<XrView> views_;
    bool running_{},focused_{},quit_{};
#define DECLARE_XR(name) PFN_##name name##_{};
    LLAMA_XR_FUNCTIONS(DECLARE_XR)
#undef DECLARE_XR
};
