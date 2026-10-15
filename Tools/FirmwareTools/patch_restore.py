"""Apply fail-closed update guard to the pinned upstream source; no fuzzy matching."""
from pathlib import Path
import sys

def replace_once(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError('Upstream restore contract changed: '+repr(old[:90]))
    return text.replace(old,new,1)

def patch(text):
    text=replace_once(text,'#define VERSION_XML "version.xml"','#define VERSION_XML "version.xml"\n\n/* IPA Desktop: opt-in strict update guard, checked before any restore transfer. */\nstatic int ipa_desktop_update_only = 0;')
    text=replace_once(text,'\t{ "erase",          no_argument,       NULL, \'e\' },','\t{ "ipa-desktop-update-only", no_argument, NULL, 1000 },\n\t{ "erase",          no_argument,       NULL, \'e\' },')
    text=replace_once(text,'\t"OPTIONS:\\n" \\','\t"OPTIONS:\\n" \\\n\t"  --ipa-desktop-update-only  Refuse erase fallback, downgrade and unknown update state\\n" \\')
    text=replace_once(text,"\t\tcase 'e':","\t\tcase 1000:\n\t\t\tipa_desktop_update_only = 1;\n\t\t\tbreak;\n\n\t\tcase 'e':")
    text=replace_once(text,'\tint tss_enabled = 0;','''\tif (ipa_desktop_update_only && (!client || (client->flags & (FLAG_ERASE | FLAG_CUSTOM | FLAG_PWN | FLAG_LATEST | FLAG_ALLOW_RESTORE_MODE | FLAG_IGNORE_ERRORS)))) {
        logger(LL_ERROR, "IPA Desktop: conflicting options for update-only restore.\\n");
        return -1;
    }
\tint tss_enabled = 0;''')
    text=replace_once(text, '\tif (client->mode == MODE_WTF) {', '''    if (ipa_desktop_update_only && client->mode != MODE_NORMAL) {
        logger(LL_ERROR, "IPA Desktop: update must start in normal mode.\\n");
        return -1;
    }

\tif (client->mode == MODE_WTF) {''')
    marker='\tclient->macos_variant = build_manifest_get_build_identity_for_model_with_variant(client->build_manifest, client->device->hardware_model, RESTORE_VARIANT_MACOS_RECOVERY_OS, 1);'
    guard='''    if (ipa_desktop_update_only) {
        plist_t info = plist_dict_get_item(build_identity, "Info");
        const char* behavior = plist_get_string_ptr(plist_dict_get_item(info, "RestoreBehavior"), NULL);
        const char* variant = plist_get_string_ptr(plist_dict_get_item(info, "Variant"), NULL);
        if ((client->flags & FLAG_ERASE) || client->mode != MODE_NORMAL ||
            !behavior || strcmp(behavior, "Update") ||
            !variant || strcmp(variant, "Customer " RESTORE_VARIANT_UPGRADE_INSTALL) ||
            !client->device_version || !client->version ||
            compare_versions(client->device_version, client->version) > 0) {
            logger(LL_ERROR, "IPA Desktop: update-only safety check failed. Erase fallback is forbidden.\\n");
            return -1;
        }
    }

'''
    return replace_once(text,marker,guard+marker)

if __name__=='__main__':
    path=Path(sys.argv[1])
    path.write_text(patch(path.read_text()))
