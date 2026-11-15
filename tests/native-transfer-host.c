#include "dnd_managed.h"
#include <stdio.h>
void dnd_console_write_line(const char *text){puts(text);}
void dnd_console_write_managed_line(DndString *text){for(uint32_t i=0;i<text->length;i++)putchar(text->chars[i]);putchar('\n');}
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv){static unsigned char memory[1048576];DndManagedHeap heap;(void)argv;
 dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>1);intptr_t result=dnd_value_aot_entry(&heap);
 if(result||dnd_exception_pending()){fprintf(stderr,"Transfer failed result=%ld kind=%d: %s\n",(long)result,dnd_exception_kind(),dnd_exception_message());return 1;}
 printf("TRANSFER passed heap=%zu collections=%zu capacity=%zu\n",heap.used,heap.collections,sizeof(memory));return 0;
}
