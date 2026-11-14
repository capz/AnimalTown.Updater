/* Public synthetic seed patterns and fixed calendars test production updater
 * control flow only. They are never linked into a console or release build. */
#include "dnd_managed.h"
#include "dnd_async.h"
#include "dnd_platform_entropy.h"
#include "dnd_tls.h"
#include "dnd_verified_copy.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define ROOT_BYTES 178673u
#define OBSERVED_ARRAYS 16
static DndObject *observed[OBSERVED_ARRAYS];
static int arrays,root_arrays,seed_arrays,clock_reads,entropy_calls,tls_calls,service_calls;
static size_t peak_used;
extern intptr_t dnd_value_aot_entry(DndManagedHeap *heap);
extern intptr_t dnd_value_Program_RequestCancellation(void);

/* Fixture-only observations are inserted into a temporary runtime copy. */
void dnd_bootstrap_observe_array(DndArray *array){
    if(array->element_size!=1||array->elements_are_references)return;
    if(array->length!=48&&array->length!=ROOT_BYTES)return;
    assert(arrays<OBSERVED_ARRAYS);
    observed[arrays++]=(DndObject *)array;
    if(array->length==48)seed_arrays++;else root_arrays++;
}
void dnd_bootstrap_observe_heap(DndManagedHeap *heap){
    if(heap->used>peak_used)peak_used=heap->used;
}

int64_t dnd_calendar_read(void){
    clock_reads++;
    /* Calls 1 and 9 are invalid clocks: the first must reject before any
     * entropy collection; the later change must clear already loaded roots. */
    if(clock_reads==1||clock_reads==9)return INT64_C(1672531200); /* 2023-01-01 */
    if(clock_reads==3||clock_reads==5||clock_reads==11)
        return INT64_C(1794657690); /* 2026-11-14 12:01:30 local */
    return INT64_C(1794657600); /* 2026-11-14 12:00:00 local */
}

int dnd_platform_entropy_fill_array(DndArray *output,int32_t timeout_ms){
    static const int expected_reads[]={2,4,6,7,8,10};
    assert(entropy_calls<6&&clock_reads==expected_reads[entropy_calls]);
    assert(!dnd_async_running()||dnd_async_context()==1);
    if(dnd_async_running())service_calls++;
    assert(output&&output->length==48&&output->element_size==1&&timeout_ms==30000);
    entropy_calls++;
    for(uint32_t i=0;i<output->length;i++)output->data[i]=(unsigned char)(i+entropy_calls);
    if(entropy_calls==3)return DND_ENTROPY_SOURCE;
    if(entropy_calls==4){
        (void)dnd_value_Program_RequestCancellation();
        assert(!dnd_exception_pending());
    }
    return DND_ENTROPY_OK;
}

int dnd_net_tls_configure_local_clock(DndArray *roots,DndArray *seed,int64_t local_seconds){
    static const unsigned char expected_hash[32]={
        0x73,0x75,0x17,0xc3,0xf0,0x3d,0xe7,0x64,0x42,0x48,0x2d,0x7d,0x4f,0xd5,0xd8,0x1d,
        0xe6,0x0d,0xa1,0xbe,0x75,0x82,0x3d,0x87,0x8a,0xdb,0x08,0xda,0x3c,0x0e,0xf4,0xfe};
    tls_calls++;
    assert(tls_calls<=3);
    assert(roots&&roots->length==ROOT_BYTES&&roots->element_size==1);
    assert(seed&&seed->length==48&&seed->element_size==1);
    assert(local_seconds==INT64_C(1794657690)); /* Fresh second read, not preflight. */
    assert(clock_reads==(tls_calls==1?3:tls_calls==2?5:11));
    assert(entropy_calls==(tls_calls==1?1:tls_calls==2?2:6));
    for(uint32_t i=0;i<seed->length;i++)assert(seed->data[i]==(unsigned char)(i+entropy_calls));
    unsigned count=0;static const char marker[]="-----BEGIN CERTIFICATE-----";
    for(uint32_t i=0;i+sizeof(marker)-1<=roots->length;i++)
        if(!memcmp(roots->data+i,marker,sizeof(marker)-1))count++;
    assert(count==118);
    DndSha256 hash;unsigned char digest[32];
    dnd_sha256_init(&hash);dnd_sha256_update(&hash,roots->data,roots->length);dnd_sha256_finish(&hash,digest);
    assert(!memcmp(digest,expected_hash,sizeof(digest)));
    /* Deliberately retain both arrays unmodified: the real managed SDK and
     * updater finally blocks must erase them on success and failure. */
    return tls_calls==2?-1:0;
}

void dnd_console_write_line(const char *text){puts(text);}

int main(int argc,char **argv){
    (void)argv;
    static unsigned char memory[1048576];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));dnd_gc_set_stress(argc>1);
    DndObject **slots[OBSERVED_ARRAYS];DndRootSet roots;DndGcRootRegistration registration={0};
    dnd_roots_init(&roots,slots,OBSERVED_ARRAYS);
    for(int i=0;i<OBSERVED_ARRAYS;i++)assert(dnd_root_add(&roots,&observed[i]));
    assert(dnd_gc_register_roots(&registration,&roots));
    intptr_t result=dnd_value_aot_entry(&heap);
    if(result||dnd_exception_pending()){
        fprintf(stderr,"Bootstrap AOT: result=%ld clocks=%d entropy=%d TLS=%d exception=%s\n",
                (long)result,clock_reads,entropy_calls,tls_calls,dnd_exception_message());
        DndException *error=(DndException *)dnd_exception_object();
        if(error&&error->message){for(uint32_t i=0;i<error->message->length;i++)fputc(error->message->chars[i],stderr);fputc('\n',stderr);}
        return 1;
    }
    assert(clock_reads==11&&entropy_calls==6&&tls_calls==3);
    assert(arrays==10&&seed_arrays==6&&root_arrays==4);
    assert(service_calls==(getenv("DND_ASYNC_MANUAL")?0:6));
    for(int i=0;i<arrays;i++){
        DndArray *array=(DndArray *)observed[i];
        for(uint32_t j=0;j<array->length;j++)assert(array->data[j]==0);
    }
    dnd_gc_unregister_roots(&registration);
    printf("BOOTSTRAP_AOT heap=%zu peak=%zu roots=%u certificates=118 entropy=48 clocks=%d entropyCalls=%d tlsCalls=%d cleared=%d serviceCalls=%d collections=%zu\n",
           heap.capacity,peak_used,ROOT_BYTES,clock_reads,entropy_calls,tls_calls,arrays,service_calls,heap.collections);
    return 0;
}
