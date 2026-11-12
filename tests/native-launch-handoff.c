/* The real pinned SDK produces an outgoing DOL _arg entry, then this fixture
 * models libogc's documented CRT relocation/table construction and exercises
 * the incoming bridge. It does not execute PowerPC code or claim hardware I/O. */
#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "dnd_dol.h"
#include "../tools/native-launch-arguments.h"

enum { BASE=0x80004000u, ABI=BASE+64u, COMMAND=BASE+256u, TABLE=BASE+4608u };
static const uint8_t arguments[]="dvd:/animal-town-updater.dol\0--installed-image\0gcloader:/Games/My AnimalTown \xc3\xa9.nkit.iso";
static unsigned launches;
static void word(uint8_t *p,uint32_t value)
{p[0]=(uint8_t)(value>>24);p[1]=(uint8_t)(value>>16);p[2]=(uint8_t)(value>>8);p[3]=(uint8_t)value;}
bool dnd_dol_platform_supported(void){return true;}
size_t dnd_dol_platform_stub_size(void){return 4;}
void dnd_dol_platform_stub_copy(void *destination){memset(destination,0xab,4);}
void dnd_fs_close(int token){(void)token;assert(!"in-memory fixture must not open a file");}
void dnd_dol_platform_launch(DndDolPlan *plan,void *image,void *stub)
{
    const uint8_t *entry=(const uint8_t *)image+256;
    assert(plan->entry==0x80004000u&&((const uint8_t *)stub)[0]==0xab);
    assert(at_launch_u32(entry+4)==0x5f617267u);
    assert(at_launch_u32(entry+8)==0x5f617267u);
    assert(at_launch_u32(entry+12)==DND_DOL_ARGUMENTS_ADDRESS);
    assert(at_launch_u32(entry+16)==sizeof(arguments));
    assert(at_launch_u32(entry+20)==0&&at_launch_u32(entry+24)==0&&at_launch_u32(entry+28)==0);
    assert(plan->arguments_size==sizeof(arguments)&&!memcmp(plan->arguments,arguments,sizeof(arguments)));
    uint8_t memory[8192]={0},copied[4096]={0};
    uint8_t *abi=memory+(ABI-BASE);word(abi,0x5f617267u);
    word(abi+4,COMMAND);word(abi+8,plan->arguments_size);
    memcpy(memory+(COMMAND-BASE),plan->arguments,plan->arguments_size);
    /* libogc __CheckARGV copies the packed commandLine and build_argv writes
     * the argc/argv/endARGV words before the executable's main is entered. */
    unsigned argc=0;size_t cursor=0;
    while(cursor<plan->arguments_size){
        word(memory+(TABLE-BASE)+argc*4,COMMAND+(uint32_t)cursor);argc++;
        while(cursor<plan->arguments_size&&plan->arguments[cursor])cursor++;
        cursor++;
    }
    word(abi+12,argc);word(abi+16,TABLE);word(abi+20,TABLE+(argc+1)*4);
    AtLaunchMemory view={memory,BASE,sizeof(memory)};
    assert(at_launch_arguments_copy(&view,ABI,copied,sizeof(copied))==(int)sizeof(arguments));
    assert(!memcmp(copied,arguments,sizeof(arguments)));launches++;
}
static DndArray *array(const uint8_t *source,size_t length)
{
    DndArray *a=calloc(1,sizeof(*a)+length);assert(a);
    a->element_size=1;a->length=(uint32_t)length;memcpy(a->data,source,length);return a;
}
int main(void)
{
    uint8_t image[288]={0};
    word(image,256);word(image+0x48,0x80004000u);word(image+0x90,32);
    word(image+0xd8,0x80006000u);word(image+0xdc,128);word(image+0xe0,0x80004000u);
    word(image+260,0x5f617267u);
    DndArray *dol=array(image,sizeof(image)),*args=array(arguments,sizeof(arguments));
    int token=dnd_dol_prepare(dol);assert(token>0);
    assert(dnd_dol_arguments(token,args)==0);
    assert(dnd_dol_request(token)==0);assert(dnd_dol_pending());dnd_dol_finish(true);
    assert(launches==1&&!dnd_dol_pending());
    /* Unsupported CRTs fail before a handoff can silently lose image paths. */
    word(dol->data+260,0);token=dnd_dol_prepare(dol);assert(token>0);
    assert(dnd_dol_arguments(token,args)==-9);assert(dnd_dol_dispose(token)==0);
    word(dol->data+260,0x5f617267u);token=dnd_dol_prepare(dol);assert(token>0);
    args->data[0]=0;assert(dnd_dol_arguments(token,args)==-10);assert(dnd_dol_dispose(token)==0);
    free(args);free(dol);
    puts("Launch argv handoff: real SDK DOL producer, _arg entry ABI and post-CRT bridge round trip passed");
    return 0;
}
