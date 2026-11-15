#include "dnd_managed.h"
#include <stdio.h>
#include <stdlib.h>
#include "bench.c"
void dnd_console_write_line(const char *text){
#ifndef GEKKO
  puts(text);
#else
  (void)text;
#endif
}
void dnd_console_write_managed_line(DndString *text){
#ifndef GEKKO
  for(uint32_t i=0;i<text->length;i++)putchar(text->chars[i]);putchar('\n');
#else
  (void)text;
#endif
}
volatile intptr_t benchmark_result,benchmark_stage;
int main(void){
  unsigned char *memory=malloc(1048576);if(!memory)return 2;DndManagedHeap heap;
  dnd_managed_heap_init(&heap,memory,1048576);
  benchmark_stage=2;benchmark_result=dnd_value_aot_entry(&heap);
  if(dnd_exception_pending())benchmark_result=100+dnd_exception_kind();
  benchmark_stage=3;
#ifdef GEKKO
  for(;;)__asm__ volatile("nop");
#else
  free(memory);return (int)benchmark_result;
#endif
}
