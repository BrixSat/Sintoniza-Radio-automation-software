// Sintoniza Pro.app, Sintoniza Rec.app, Sintoniza Import.app: start their program inside Sintoniza Emi.app (one
// .NET runtime for all). Sintoniza Emi.app is looked for next to this app (both in Applications), then in
// /Applications and ~/Applications, also under its name before. build.sh compiles it once per app:
//   clang -DPROGRAM='"SintonizaPro"' -DARGUMENT='""' -DTITLE='"Sintoniza Pro"' launcher.c -o sintoniza-schedule
// A small program rather than a script, so it is signed and notarized like any other.
#include <libgen.h>
#include <limits.h>
#include <stdint.h>
#include <mach-o/dyld.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

static int try_start(const char *app, int argc, char **argv)
{
    static const char *names[] = { PROGRAM, "RadioPlayout" }; // the main program's name before, for --import
    for (int n = 0; n < (strlen(ARGUMENT) > 0 ? 2 : 1); n++) {
        char exe[PATH_MAX];
        snprintf(exe, sizeof exe, "%s/Contents/MacOS/%s", app, names[n]);
        if (access(exe, X_OK) != 0) continue;
        char **args = calloc((size_t)argc + 2, sizeof(char *));
        int i = 0;
        args[i++] = exe;
        if (strlen(ARGUMENT) > 0) args[i++] = (char *)ARGUMENT;
        for (int a = 1; a < argc; a++) args[i++] = argv[a];
        args[i] = NULL;
        execv(exe, args);
        free(args);
    }
    return -1;
}

int main(int argc, char **argv)
{
    char self[PATH_MAX], real[PATH_MAX];
    uint32_t size = sizeof self;
    if (_NSGetExecutablePath(self, &size) != 0 || realpath(self, real) == NULL) strcpy(real, "/Applications/x.app/Contents/MacOS/x");
    // …/Applications/Sintoniza Pro.app/Contents/MacOS/sintoniza-schedule → …/Applications
    char *dir = dirname(dirname(dirname(dirname(real))));
    const char *home = getenv("HOME");
    const char *apps[] = { "Sintoniza Emi.app", "Sintoniza Radio Broadcast system.app" };
    for (int a = 0; a < 2; a++) {
        char path[PATH_MAX];
        snprintf(path, sizeof path, "%s/%s", dir, apps[a]);
        try_start(path, argc, argv);
        snprintf(path, sizeof path, "/Applications/%s", apps[a]);
        try_start(path, argc, argv);
        if (home) {
            snprintf(path, sizeof path, "%s/Applications/%s", home, apps[a]);
            try_start(path, argc, argv);
        }
    }
    system("osascript -e 'display alert \"" TITLE "\" message \"Put " TITLE " in the same folder as Sintoniza Emi (Applications).\"'");
    return 1;
}
