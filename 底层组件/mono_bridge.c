#include "mono_bridge.h"

#include <string.h>

/* Well-known Mono module names, tried in order (Unity 2018+ -> legacy). */
static const wchar_t *const kMonoModuleNames[] = {
    SSJJ_MONO_NAME_BDWGC,   /* Unity 2018+ (MonoBleedingEdge/EmbedRuntime) */
    SSJJ_MONO_NAME_SGEN,    /* standard Mono distributions                 */
    SSJJ_MONO_NAME_LEGACY,  /* Unity <= 2017                               */
    SSJJ_MONO_NAME_V2,      /* legacy fallback                             */
    NULL
};

HMODULE ssjj_find_mono_module(wchar_t *module_path, size_t path_capacity) {
    int i;

    for (i = 0; kMonoModuleNames[i] != NULL; ++i) {
        HMODULE module = GetModuleHandleW(kMonoModuleNames[i]);
        if (module == NULL) {
            continue;
        }
        if (module_path != NULL && path_capacity != 0) {
            DWORD length = GetModuleFileNameW(
                    module, module_path, (DWORD)path_capacity);
            if (length == 0 || length >= path_capacity) {
                module_path[0] = L'\0';
            }
        }
        return module;
    }
    return NULL;
}

static void *resolve_export(HMODULE module, const char *name) {
    return (void *)GetProcAddress(module, name);
}

int ssjj_mono_api_init(ssjj_mono_api *api, HMODULE module) {
    if (api == NULL || module == NULL) {
        return 0;
    }
    memset(api, 0, sizeof(*api));
    api->module = module;
    GetModuleFileNameW(module, api->module_path, MAX_PATH);

    api->mono_get_root_domain =
            (MonoDomain *(*)(void))resolve_export(module, "mono_get_root_domain");
    api->mono_thread_attach =
            (MonoThread *(*)(MonoDomain *))resolve_export(module, "mono_thread_attach");
    api->mono_image_open_from_data_with_name =
            (MonoImage *(*)(char *, guint32, gboolean, MonoImageOpenStatus *,
                    const char *))resolve_export(module,
                    "mono_image_open_from_data_with_name");
    api->mono_assembly_load_from_full =
            (MonoAssembly *(*)(MonoImage *, const char *, MonoImageOpenStatus *,
                    gboolean))resolve_export(module, "mono_assembly_load_from_full");
    api->mono_domain_assembly_open =
            (MonoAssembly *(*)(MonoDomain *, const char *))resolve_export(module,
                    "mono_domain_assembly_open");
    api->mono_assembly_get_image =
            (MonoImage *(*)(MonoAssembly *))resolve_export(module,
                    "mono_assembly_get_image");
    api->mono_class_from_name =
            (MonoClass *(*)(MonoImage *, const char *, const char *))resolve_export(
                    module, "mono_class_from_name");
    api->mono_class_get_method_from_name =
            (MonoMethod *(*)(MonoClass *, const char *, int))resolve_export(module,
                    "mono_class_get_method_from_name");
    api->mono_runtime_invoke =
            (MonoObject *(*)(MonoMethod *, void *, void **, MonoObject **))resolve_export(
                    module, "mono_runtime_invoke");
    api->mono_string_new =
            (MonoString *(*)(MonoDomain *, const char *))resolve_export(module,
                    "mono_string_new");
    api->mono_image_get_name =
            (const char *(*)(MonoImage *))resolve_export(module, "mono_image_get_name");

    /* Core exports must all be present, otherwise the module is not a
     * usable Mono embedding runtime. */
    if (api->mono_get_root_domain == NULL
            || api->mono_thread_attach == NULL
            || api->mono_class_from_name == NULL
            || api->mono_class_get_method_from_name == NULL
            || api->mono_runtime_invoke == NULL) {
        return 0;
    }
    return 1;
}
