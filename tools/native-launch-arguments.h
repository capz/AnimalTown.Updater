/* Shared game/updater reader for the six-word, big-endian libogc _arg ABI.
 * All addresses are checked against an explicit memory window before reading.
 * The host tests use the same validator with synthetic 32-bit MEM1 addresses. */
#ifndef ANIMALTOWN_LAUNCH_ARGUMENTS_H
#define ANIMALTOWN_LAUNCH_ARGUMENTS_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>

typedef struct { const uint8_t *bytes;uint32_t base;size_t length; } AtLaunchMemory;
static inline const uint8_t *at_launch_pointer(const AtLaunchMemory *m,uint32_t address,size_t bytes)
{
    if(!m||!m->bytes||(uint64_t)m->length>UINT64_C(0x100000000)-m->base||address<m->base||bytes>m->length||(size_t)(address-m->base)>m->length-bytes)return NULL;
    return m->bytes+(address-m->base);
}
static inline uint32_t at_launch_u32(const uint8_t *p)
{return (uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];}
static inline int at_launch_utf8(const uint8_t *p,size_t length)
{
    for(size_t i=0;i<length;){
        uint8_t first=p[i++];uint32_t value;unsigned more;
        if(first<0x80)continue;
        if(first>=0xc2&&first<=0xdf){value=first&31;more=1;}
        else if(first>=0xe0&&first<=0xef){value=first&15;more=2;}
        else if(first>=0xf0&&first<=0xf4){value=first&7;more=3;}
        else return 0;
        if(more>length-i)return 0;
        for(unsigned n=0;n<more;n++){uint8_t c=p[i++];if((c&0xc0)!=0x80)return 0;value=value<<6|(c&63);}
        if((more==1&&value<0x80)||(more==2&&value<0x800)||(more==3&&value<0x10000)||
           value>0x10ffff||(value>=0xd800&&value<=0xdfff))return 0;
    }
    return 1;
}
/* 0=no loader arguments; -1=malformed/unsupported arguments. Neither failure
 * modifies destination. Success returns the complete NUL-separated UTF-8 argv. */
static inline int at_launch_arguments_copy(const AtLaunchMemory *m,uint32_t abi,uint8_t *destination,size_t capacity)
{
    if(!abi)return 0;
    if(abi&3)return -1;
    const uint8_t *a=at_launch_pointer(m,abi,24);if(!a)return -1;
    if(at_launch_u32(a)!=0x5f617267u)return 0;
    uint32_t command=at_launch_u32(a+4),length=at_launch_u32(a+8),argc=at_launch_u32(a+12);
    uint32_t table=at_launch_u32(a+16),end=at_launch_u32(a+20);
    if(!argc)return 0;
    if(argc>64||length<2||length>4096||!destination||length>capacity||(table&3)||(end&3))return -1;
    const uint8_t *text=at_launch_pointer(m,command,length),*pointers=at_launch_pointer(m,table,((size_t)argc+1)*4);
    uint64_t table_end=(uint64_t)table+((uint64_t)argc+1)*4;
    if(!text||!pointers||table_end>UINT32_MAX||end<table_end||!at_launch_pointer(m,end,0)||
       at_launch_u32(pointers+argc*4)!=0)return -1;
    size_t cursor=0;
    for(uint32_t i=0;i<argc;i++){
        if(cursor>=length||(uint64_t)command+cursor>UINT32_MAX||at_launch_u32(pointers+i*4)!=(uint32_t)(command+cursor))return -1;
        size_t start=cursor;while(cursor<length&&text[cursor])cursor++;
        if(cursor==start||cursor>=length||!at_launch_utf8(text+start,cursor-start))return -1;
        cursor++;
    }
    if(cursor!=length)return -1;
    memmove(destination,text,length);return (int)length;
}
#endif
