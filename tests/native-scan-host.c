#define _POSIX_C_SOURCE 200809L
#include "dnd_storage.h"
#include <assert.h>
#include <dirent.h>
#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Read-only diagnostics use the private block shape of the pinned runtime.
 * The runner checks that shape before inserting nonallocating observations. */
typedef struct Block {uint32_t size;uint8_t marked,free;uint16_t reserved;struct Block *next;} Block;
static size_t peak_used,total_allocated,allocations,peak_post_gc_live;
static size_t entries,opens,header_bytes,highest_header_bytes,highest_entry_index;
static int highest_handle;
static bool in_scan_read,fault_observed;
static unsigned injected;
#ifdef DND_SCAN_NATIVE_FAULT
struct dirent *__real_readdir(DIR *directory);
struct dirent *__wrap_readdir(DIR *directory)
{
    if(in_scan_read&&!injected&&getenv("DND_SCAN_NATIVE_MEMORY_FAULT")){
        in_scan_read=false;injected++;errno=ENOMEM;return NULL;
    }
    return __real_readdir(directory);
}
#endif
DndString *dnd_scan_dir_next(DndManagedHeap *heap,int handle,int kind)
{
    in_scan_read=true;DndString *path=dnd_fs_dir_next(heap,handle,kind);in_scan_read=false;
    if(path)entries++;
    return path;
}
int dnd_scan_open(DndString *path,int mode,int access,int share)
{
    static const char suffix[]="/AnimalTown-12.34.56.nkit.iso";
    int handle=dnd_fs_open(path,mode,access,share);opens++;
    bool highest=path&&path->length>=sizeof(suffix)-1;
    for(size_t i=0;highest&&i<sizeof(suffix)-1;i++)
        if(path->chars[path->length-(sizeof(suffix)-1)+i]!=(unsigned char)suffix[i])highest=false;
    if(highest){highest_handle=handle;highest_entry_index=entries;}
    return handle;
}
int dnd_scan_read(int handle,DndArray *data,int offset,int count)
{
    int n=dnd_fs_read(handle,data,offset,count);
    if(n>0){header_bytes+=(size_t)n;if(handle==highest_handle)highest_header_bytes+=(size_t)n;}
    return n;
}
void dnd_scan_close(int handle){if(handle==highest_handle)highest_handle=0;dnd_fs_close(handle);}
void dnd_scan_heap_allocated(DndManagedHeap *heap,size_t bytes)
{
    allocations++;total_allocated+=bytes;
    if(heap->used>peak_used)peak_used=heap->used;
}
void dnd_scan_heap_collected(DndManagedHeap *heap)
{
    size_t live=0;
    for(Block *block=heap->blocks;block;block=block->next)if(!block->free)live+=((sizeof(Block)+7u)&~7u)+block->size;
    if(live>peak_post_gc_live)peak_post_gc_live=live;
}
void dnd_console_write_line(const char *text){
    if(!strcmp(text,"Native enumeration ENOMEM retained before transfer"))fault_observed=true;
    puts(text);
}
void dnd_console_write_managed_line(DndString *text)
{
    static const char marker[]="Native enumeration ENOMEM retained before transfer";
    bool match=text->length==sizeof(marker)-1;
    for(size_t i=0;match&&i<sizeof(marker)-1;i++)if(text->chars[i]!=(unsigned char)marker[i])match=false;
    if(match)fault_observed=true;
    for(uint32_t i=0;i<text->length;i++)fputc((char)text->chars[i],stdout);
    fputc('\n',stdout);
}
extern intptr_t dnd_value_aot_entry(DndManagedHeap *);
int main(int argc,char **argv)
{
    (void)argv;size_t capacity=1024*1024;
    const char *setting=getenv("DND_SCAN_HEAP_BYTES");if(setting)capacity=(size_t)strtoull(setting,NULL,10);
    assert(capacity>=128*1024&&capacity<=4*1024*1024);
    unsigned char *memory=malloc(capacity);assert(memory);DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,capacity);dnd_gc_set_stress(argc>1);
    intptr_t result=dnd_value_aot_entry(&heap);
    int exception=dnd_exception_pending()?dnd_exception_kind():0,handles=dnd_fs_open_handles();
    printf("SCAN_HEAP capacity=%zu peak_used=%zu peak_post_gc_live=%zu total_allocated=%zu allocations=%zu collections=%zu final_used=%zu result=%ld exception=%d handles=%d\n",
           capacity,peak_used,peak_post_gc_live,total_allocated,allocations,heap.collections,heap.used,(long)result,exception,handles);
    printf("SCAN_IO entries=%zu image_opens=%zu header_bytes=%zu highest_header_bytes=%zu highest_entry_index=%zu\n",entries,opens,header_bytes,highest_header_bytes,highest_entry_index);
    if(result||dnd_exception_pending()||handles){
        fprintf(stderr,"Native scan failed: exception=%d message=%s\n",exception,dnd_exception_message());
        DndException *error=(DndException*)dnd_exception_object();
        if(error&&error->message){for(uint32_t i=0;i<error->message->length;i++)fputc(error->message->chars[i],stderr);fputc('\n',stderr);}
        free(memory);return 1;
    }
    if(getenv("DND_SCAN_NATIVE_MEMORY_FAULT")){
        assert(injected==1&&fault_observed&&entries==0&&opens==0&&header_bytes==0&&peak_used<128*1024);
        puts("Native ENOMEM fault: real storage mapping, retained managed Message, low heap, phase-before-transfer and handle cleanup passed");
    }else{
        assert(!injected&&!fault_observed&&highest_header_bytes==0x440);
        setting=getenv("DND_SCAN_EXPECT_ENTRIES");if(setting)assert(entries==(size_t)strtoull(setting,NULL,10));
    }
    free(memory);return 0;
}
