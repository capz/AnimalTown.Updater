#include "dnd_managed.h"
#include "animaltown_launch_arguments.h"
#ifdef DND_GAMECUBE_STORAGE
#include <gctypes.h>
_Static_assert(sizeof(struct __argv)==24,"libogc argv ABI changed");
_Static_assert(offsetof(struct __argv,argc)==12&&offsetof(struct __argv,argv)==16&&offsetof(struct __argv,endARGV)==20,"libogc argv field offsets changed");
#endif

int animaltown_launch_arguments_copy(DndArray *destination)
{
    if(!destination||destination->element_size!=1||destination->elements_are_references)return -1;
#ifdef DND_GAMECUBE_STORAGE
    /* libogc2's CRT validates _arg and copies strings/tables into MEM1 before
     * main. Never scan RAM or derive a filename from disc title/banner text. */
    uintptr_t abi=(uintptr_t)__system_argv;
    if(abi>UINT32_MAX)return -1;
    AtLaunchMemory memory={(const uint8_t *)(uintptr_t)0x80003100u,0x80003100u,0x01800000u-0x3100u};
    return at_launch_arguments_copy(&memory,(uint32_t)abi,destination->data,destination->length);
#else
    return 0;
#endif
}
