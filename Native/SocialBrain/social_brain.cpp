#include <rac/core/rac_core.h>
#include <rac/core/rac_platform_adapter.h>
#include <rac/backends/rac_llm_llamacpp.h>
#include <cstring>
#include <cstdlib>
#include <mutex>
#include <string>
#include <exception>
#include <filesystem>
#include <chrono>
#include <cstdio>

#define FR_EXPORT extern "C" __declspec(dllexport)
struct Brain { rac_handle_t model = nullptr; std::mutex generation; };
static std::mutex lifecycle;
static bool active = false;
static rac_platform_adapter_t platform{};
static char* copy(const char* value) {
    if (!value) value = "";
    auto size = std::strlen(value) + 1;
    auto result = static_cast<char*>(std::malloc(size));
    if (result) std::memcpy(result, value, size);
    return result;
}
FR_EXPORT const char* fr_social_version() { return "runanywhere-0.20.39-cpu-bridge-2"; }
FR_EXPORT int fr_social_create(const char* path, int gpu_layers, void** out, char** error) noexcept {
    if (!out || !error || !path) return -1;
    *out = nullptr; *error = nullptr;
    std::lock_guard<std::mutex> guard(lifecycle);
    if (active) { *error = copy("One resident social runtime is allowed"); return -1; }
    // The published Windows kit contains ggml-cpu only. Never silently report GPU success.
    if (gpu_layers != 0) { *error = copy("Pinned RunAnywhere Windows kit has no CUDA backend; rebuild with CUDA or use CPU"); return -1; }
    Brain* brain = nullptr;
    try {
        platform = {};
        platform.abi_version = RAC_PLATFORM_ADAPTER_ABI_VERSION;
        platform.struct_size = sizeof(platform);
        platform.file_exists = [](const char* p, void*) -> rac_bool_t { return std::filesystem::exists(std::filesystem::u8path(p)) ? RAC_TRUE : RAC_FALSE; };
        platform.file_read = [](const char*, void** data, size_t* size, void*) -> rac_result_t { if (data) *data = nullptr; if (size) *size = 0; return RAC_ERROR_NOT_SUPPORTED; };
        platform.file_write = [](const char*, const void*, size_t, void*) -> rac_result_t { return RAC_ERROR_NOT_SUPPORTED; };
        platform.file_delete = [](const char*, void*) -> rac_result_t { return RAC_ERROR_NOT_SUPPORTED; };
        platform.secure_get = [](const char*, char** value, void*) -> rac_result_t { if (value) *value = nullptr; return RAC_ERROR_FILE_NOT_FOUND; };
        platform.secure_set = [](const char*, const char*, void*) -> rac_result_t { return RAC_ERROR_NOT_SUPPORTED; };
        platform.secure_delete = [](const char*, void*) -> rac_result_t { return RAC_ERROR_NOT_SUPPORTED; };
        platform.log = [](rac_log_level_t level, const char* category, const char* message, void*) { if (level >= RAC_LOG_ERROR) std::fprintf(stderr, "[%s] %s\n", category, message); };
        platform.now_ms = [](void*) -> int64_t { return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count(); };
        rac_config_t init{};
        init.platform_adapter = &platform;
        init.log_level = RAC_LOG_ERROR;
        init.log_tag = "FratRowSocial";
        auto code = rac_init(&init);
        if (code != RAC_SUCCESS) { *error = copy(rac_error_get_details()); return code; }
        brain = new Brain();
        auto config = RAC_LLM_LLAMACPP_CONFIG_DEFAULT;
        config.context_size = 4096;
        config.num_threads = 6;
        config.gpu_layers = 0;
        config.batch_size = 128;
        code = rac_llm_llamacpp_create(path, &config, &brain->model);
        if (code != RAC_SUCCESS) {
            delete brain; rac_shutdown(); *error = copy("GGUF load failed"); return code;
        }
        active = true; *out = brain; return 0;
    } catch (const std::exception& e) {
        if (brain) { if (brain->model) rac_llm_llamacpp_destroy(brain->model); delete brain; }
        rac_shutdown(); *error = copy(e.what()); return -1;
    } catch (...) { rac_shutdown(); *error = copy("Unknown native load exception"); return -1; }
}
FR_EXPORT int fr_social_generate_configured(void* handle, const char* prompt, const char* system_prompt, const char* grammar, int seed, float temperature,
    char** text, char** error, int* prompt_tokens, int* output_tokens) noexcept {
    if (!handle || !prompt || !grammar || !text || !error) return -1;
    *text = nullptr; *error = nullptr;
    auto brain = static_cast<Brain*>(handle);
    std::lock_guard<std::mutex> guard(brain->generation);
    try {
        rac_llm_llamacpp_clear_context(brain->model);
        auto options = RAC_LLM_OPTIONS_DEFAULT;
        options.max_tokens = 384;
        options.temperature = temperature;
        options.top_p = 0.9f;
        options.seed = seed;
        options.disable_thinking = RAC_TRUE;
        options.system_prompt = system_prompt;
        options.grammar = grammar;
        rac_llm_result_t result{};
        auto code = rac_llm_llamacpp_generate(brain->model, prompt, &options, &result);
        if (prompt_tokens) *prompt_tokens = result.prompt_tokens;
        if (output_tokens) *output_tokens = result.completion_tokens;
        if (code == RAC_SUCCESS) *text = copy(result.text);
        else *error = copy("RunAnywhere generation failed or was cancelled");
        rac_llm_result_free(&result);
        return code;
    } catch (const std::exception& e) { *error = copy(e.what()); return -1; }
      catch (...) { *error = copy("Unknown native generation exception"); return -1; }
}
FR_EXPORT int fr_social_generate(void* handle, const char* prompt, const char* system_prompt, const char* grammar, int seed,
    char** text, char** error, int* prompt_tokens, int* output_tokens) noexcept {
    return fr_social_generate_configured(handle, prompt, system_prompt, grammar, seed, 0.4f, text, error, prompt_tokens, output_tokens);
}
FR_EXPORT void fr_social_cancel(void* handle) noexcept {
    if (handle) rac_llm_llamacpp_cancel(static_cast<Brain*>(handle)->model);
}
FR_EXPORT void fr_social_free(void* buffer) noexcept { std::free(buffer); }
FR_EXPORT void fr_social_destroy(void* handle) noexcept {
    std::lock_guard<std::mutex> guard(lifecycle);
    if (!handle) return;
    auto brain = static_cast<Brain*>(handle);
    { std::lock_guard<std::mutex> generation(brain->generation); rac_llm_llamacpp_destroy(brain->model); }
    delete brain; rac_shutdown(); active = false;
}
