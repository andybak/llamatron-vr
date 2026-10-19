#pragma once
#include <windows.h>
#include <array>
#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

struct SpriteDraw {
    int16_t x{},y{};
    uint16_t bank{},offset{},sequence{},page{};
    bool nativeEffect{};
};
struct SceneFrame {
    uint16_t sequence{},publication{};
    uint32_t eventOrder{};
    std::vector<SpriteDraw> sprites;
    std::array<uint8_t,320*200*4> effects{};
    bool hasEffects{};
};
class Capture {
public:
    explicit Capture(const std::filesystem::path& runtime);
    ~Capture();
    Capture(const Capture&)=delete;
    bool readFrame(SceneFrame& frame);
    void input(uint8_t bits,uint8_t scan=0,bool writeScan=false);
    uint32_t pid() const {return pid_;}
private:
    std::vector<uint8_t> read(uintptr_t address,size_t size) const;
    void locate();
    HANDLE process_{};
    uint32_t pid_{},hookSize_{},ringStart_{},capacity_{},bufferSegment_{};
    uint16_t caveSegment_{},gameSegment_{};
    uintptr_t ring_{},guest_{};
    bool nativeEffects_{};
};
