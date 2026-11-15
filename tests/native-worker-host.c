#include "dnd_managed.h"
#include "dnd_async.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include "worker.c"
static unsigned frames,progress_frames;
int dnd_device_begin(float r,float g,float b,float a){(void)r;(void)g;(void)b;(void)a;return 0;}
int dnd_device_fence_completed(int token){(void)token;return 1;}
int dnd_device_end(void){
    intptr_t before=dnd_static_AnimalTown_Updater_Updater_Steps;
    int context=dnd_async_app_wait_begin();
    struct timespec delay={0,20000000};nanosleep(&delay,NULL);
    dnd_async_app_wait_end(context);
    frames++;if(dnd_static_AnimalTown_Updater_Updater_Steps>before)progress_frames++;
    return 0;
}
void dnd_device_shutdown(void){}
void dnd_console_write_line(const char *text){puts(text);}
void dnd_console_write_managed_line(DndString *text){for(uint32_t i=0;i<text->length;i++)putchar(text->chars[i]);putchar('\n');}
int main(void){
    unsigned char *memory=malloc(1048576);assert(memory);DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,1048576);
    intptr_t result=dnd_value_aot_entry(&heap);
    if(result||dnd_exception_pending()){fprintf(stderr,"Worker failed: %s\n",dnd_exception_message());free(memory);return 1;}
    assert(progress_frames>0);
    printf("WORKER frames=%u frames_with_background_progress=%u steps=128 heap=%zu\n",frames,progress_frames,heap.used);
    free(memory);return 0;
}
