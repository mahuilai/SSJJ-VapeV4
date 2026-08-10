#ifndef SSJJ_MONO_BRIDGE_H
#define SSJJ_MONO_BRIDGE_H

#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ------------------------------------------------------------------ */
/* Minimal Mono runtime types (opaque, layout never dereferenced here) */
/* ------------------------------------------------------------------ */
typedef struct _MonoDomain     MonoDomain;
typedef struct _MonoAssembly   MonoAssembly;
typedef struct _MonoImage      MonoImage;
typedef struct _MonoClass      MonoClass;
typedef struct _MonoMethod     MonoMethod;
typedef struct _MonoObject     MonoObject;
typedef struct _MonoThread     MonoThread;
typedef struct _MonoString     MonoString;

typedef int         MonoImageOpenStatus;  /* MONO_IMAGE_OK == 0 */
typedef int         gboolean;             /* TRUE == 1 */
typedef unsigned int guint32;

/* ------------------------------------------------------------------ */
/* Function-pointer table, resolved at runtime from the loaded mono DLL */
/* ------------------------------------------------------------------ */
typedef struct ssjj_mono_api {
    HMODULE  module;
    wchar_t  module_path[MAX_PATH];

    MonoDomain *(*mono_get_root_domain)(void);
    MonoThread *(*mono_thread_attach)(MonoDomain *domain);

    /* in-memory assembly load */
    MonoImage *(*mono_image_open_from_data_with_name)(
            char *data, guint32 data_len, gboolean need_copy,
            MonoImageOpenStatus *status, const char *name);
    MonoAssembly *(*mono_assembly_load_from_full)(
            MonoImage *image, const char *fname,
            MonoImageOpenStatus *status, gboolean refonly);

    /* on-disk assembly load (fallback) */
    MonoAssembly *(*mono_domain_assembly_open)(MonoDomain *domain, const char *name);

    MonoImage    *(*mono_assembly_get_image)(MonoAssembly *assembly);
    MonoClass    *(*mono_class_from_name)(MonoImage *image,
            const char *name_space, const char *name);
    MonoMethod   *(*mono_class_get_method_from_name)(MonoClass *klass,
            const char *name, int param_count);
    MonoObject   *(*mono_runtime_invoke)(MonoMethod *method, void *obj,
            void **params, MonoObject **exc);
    MonoString   *(*mono_string_new)(MonoDomain *domain, const char *text);
    const char   *(*mono_image_get_name)(MonoImage *image);
} ssjj_mono_api;

/* ------------------------------------------------------------------ */
/* Well-known Mono module names, tried in order by ssjj_find_mono_module */
/* ------------------------------------------------------------------ */
#define SSJJ_MONO_NAME_BDWGC L"mono-2.0-bdwgc.dll"  /* Unity 2018+ */
#define SSJJ_MONO_NAME_SGEN  L"mono-2.0-sgen.dll"   /* standard Mono */
#define SSJJ_MONO_NAME_LEGACY L"mono.dll"           /* Unity <= 2017 */
#define SSJJ_MONO_NAME_V2    L"mono-2.0.dll"        /* legacy fallback */

/* Returns the first loaded well-known mono module (NULL if none loaded yet).
 * Optionally fills module_path with its full path. */
HMODULE ssjj_find_mono_module(wchar_t *module_path, size_t path_capacity);

/* Resolves every entry in the table from the given module.
 * Returns 1 on success (core exports present), 0 on failure. */
int ssjj_mono_api_init(ssjj_mono_api *api, HMODULE module);

#ifdef __cplusplus
}
#endif

#endif /* SSJJ_MONO_BRIDGE_H */
