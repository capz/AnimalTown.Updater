/* The host maps actual big-endian libogc _arg ABI addresses into a byte array.
 * No process memory scan or arbitrary host pointer dereference is involved. */
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "../tools/native-launch-arguments.h"

enum { BASE=0x80004000u, ABI=BASE+64u, COMMAND=BASE+256u, TABLE=BASE+4608u };
static uint8_t bytes[8192],output[4097];
static unsigned checks;
#define CHECK(x) do { assert(x);checks++; } while(0)
static void word(uint32_t address,uint32_t value)
{
    size_t at=(size_t)(address-BASE);assert(at<=sizeof(bytes)-4);
    bytes[at]=(uint8_t)(value>>24);bytes[at+1]=(uint8_t)(value>>16);
    bytes[at+2]=(uint8_t)(value>>8);bytes[at+3]=(uint8_t)value;
}
static AtLaunchMemory memory(void)
{AtLaunchMemory m={bytes,BASE,sizeof(bytes)};return m;}
static void fixture(const uint8_t *packed,size_t length,unsigned argc)
{
    memset(bytes,0,sizeof(bytes));assert(length<=4097);
    memcpy(bytes+(COMMAND-BASE),packed,length);
    word(ABI,0x5f617267u);word(ABI+4,COMMAND);word(ABI+8,(uint32_t)length);
    word(ABI+12,argc);word(ABI+16,TABLE);word(ABI+20,TABLE+(argc+1u)*4u);
    size_t cursor=0;
    for(unsigned i=0;i<argc;i++){
        word(TABLE+i*4u,COMMAND+(uint32_t)cursor);
        while(cursor<length&&packed[cursor])cursor++;
        cursor++;
    }
    word(TABLE+argc*4u,0);
}
static int copy(AtLaunchMemory *m,uint32_t abi,size_t capacity)
{
    memset(output,0xa5,sizeof(output));
    return at_launch_arguments_copy(m,abi,output,capacity);
}
static void rejected(AtLaunchMemory *m,uint32_t abi,size_t capacity,int expected)
{
    CHECK(copy(m,abi,capacity)==expected);
    for(size_t i=0;i<sizeof(output);i++)CHECK(output[i]==0xa5);
}
static void accepts(AtLaunchMemory *m,const uint8_t *packed,size_t length)
{
    CHECK(copy(m,ABI,sizeof(output))==(int)length);
    CHECK(!memcmp(output,packed,length));CHECK(output[length]==0xa5);
}
static const uint8_t ordinary[]="dvd:/Games/AnimalTown.nkit.iso\0--installed-image\0gcloader:/Games/AnimalTown.nkit.iso";
static void ordinary_fixture(void){fixture(ordinary,sizeof(ordinary),3);}

int main(void)
{
    AtLaunchMemory m=memory();
    ordinary_fixture();accepts(&m,ordinary,sizeof(ordinary));
    /* Swiss image argv[0], an outgoing updater argv and standalone DOL argv
     * are copied verbatim. Classification belongs to managed path policy. */
    static const uint8_t swiss[]="dvd:/Games/My AnimalTown.nkit.iso";
    fixture(swiss,sizeof(swiss),1);accepts(&m,swiss,sizeof(swiss));
    static const uint8_t dol[]="carda:/apps/AnimalTown.dol";
    fixture(dol,sizeof(dol),1);accepts(&m,dol,sizeof(dol));
    static const uint8_t unicode[]="gcldr:/Games/Caf\xc3\xa9 \xe7\x8c\xab \xf0\x9f\x8c\xb1.nkit.iso\0A B";
    fixture(unicode,sizeof(unicode),2);accepts(&m,unicode,sizeof(unicode));
    static const uint8_t minimum[]={'x',0};
    fixture(minimum,sizeof(minimum),1);accepts(&m,minimum,sizeof(minimum));

    ordinary_fixture();word(ABI,0);rejected(&m,ABI,sizeof(output),0);
    ordinary_fixture();word(ABI,0x01020304);rejected(&m,ABI,sizeof(output),0);
    memset(bytes,0,sizeof(bytes));word(ABI,0x5f617267u);
    rejected(&m,ABI,sizeof(output),0);
    rejected(&m,0,sizeof(output),0);
    ordinary_fixture();rejected(NULL,ABI,sizeof(output),-1);
    ordinary_fixture();m.bytes=NULL;rejected(&m,ABI,sizeof(output),-1);m=memory();
    ordinary_fixture();m.length=(size_t)UINT32_MAX;rejected(&m,ABI,sizeof(output),-1);m=memory();
    ordinary_fixture();CHECK(at_launch_arguments_copy(&m,ABI,NULL,sizeof(output))==-1);

    ordinary_fixture();word(ABI+12,65);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+12,UINT32_MAX);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+8,0);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+8,1);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+8,4097);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+4,BASE-1u);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+4,UINT32_MAX-8u);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+4,BASE+(uint32_t)sizeof(bytes)-4u);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+16,0);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+16,TABLE+1);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+16,UINT32_MAX-3u);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+16,BASE+(uint32_t)sizeof(bytes)-4u);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+20,TABLE+12);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+20,TABLE+17);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+20,BASE+(uint32_t)sizeof(bytes)+4u);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(TABLE,COMMAND+1);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(TABLE+4,COMMAND);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(TABLE+12,COMMAND);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(TABLE+4,BASE+(uint32_t)sizeof(bytes));rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();bytes[COMMAND-BASE+sizeof(ordinary)-1]='x';rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();bytes[COMMAND-BASE]=0;rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();bytes[COMMAND-BASE+2]=0;rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();word(ABI+8,(uint32_t)sizeof(ordinary)+1);rejected(&m,ABI,sizeof(output),-1);
    ordinary_fixture();rejected(&m,ABI+1,sizeof(output),-1);
    ordinary_fixture();rejected(&m,BASE+(uint32_t)sizeof(bytes)-20u,sizeof(output),-1);
    ordinary_fixture();rejected(&m,ABI,sizeof(ordinary)-1,-1);
    ordinary_fixture();rejected(&m,ABI,0,-1);
    ordinary_fixture();m.length=ABI-BASE+23u;rejected(&m,ABI,sizeof(output),-1);m=memory();
    ordinary_fixture();word(ABI+20,TABLE+24);accepts(&m,ordinary,sizeof(ordinary));

    static const uint8_t invalid[][6]={
        {0x80,0},{0xff,0},{0xc0,0x80,0},{0xc1,0xbf,0},
        {0xc2,0},{0xe0,0x80,0x80,0},{0xed,0xa0,0x80,0},
        {0xe2,0x82,0},{0xf0,0x80,0x80,0x80,0},
        {0xf4,0x90,0x80,0x80,0},{0xf5,0x80,0x80,0x80,0},
        {0xf0,0x9f,0x8c,0},{0xe2,'A',0xa1,0}
    };
    for(size_t i=0;i<sizeof(invalid)/sizeof(invalid[0]);i++){
        size_t length=1;while(invalid[i][length])length++;length++;
        fixture(invalid[i],length,1);rejected(&m,ABI,sizeof(output),-1);
    }
    /* Every UTF-8 boundary scalar is legal; surrogate halves and overlong
     * sequences above were rejected instead of repaired into another path. */
    static const uint8_t boundary[]={0x7f,0xc2,0x80,0xdf,0xbf,0xe0,0xa0,0x80,0xed,0x9f,0xbf,0xee,0x80,0x80,0xef,0xbf,0xbf,0xf0,0x90,0x80,0x80,0xf4,0x8f,0xbf,0xbf,0};
    fixture(boundary,sizeof(boundary),1);accepts(&m,boundary,sizeof(boundary));
    uint8_t maximum[4096];
    for(unsigned i=0;i<64;i++){memset(maximum+i*64,'a'+(int)(i%26),63);maximum[i*64+63]=0;}
    fixture(maximum,sizeof(maximum),64);accepts(&m,maximum,sizeof(maximum));
    CHECK(copy(&m,ABI,sizeof(maximum))==4096);CHECK(!memcmp(output,maximum,sizeof(maximum)));
    rejected(&m,ABI,sizeof(maximum)-1,-1);
    /* Fixed-seed malformed ABI fuzzing also runs under AddressSanitizer.
     * Every rejected read must leave the caller's complete buffer untouched. */
    uint32_t seed=0x41524756;
    for(unsigned i=0;i<2000;i++){
        ordinary_fixture();seed=seed*1664525u+1013904223u;
        unsigned field=(seed>>24)%6;seed=seed*1664525u+1013904223u;
        word(ABI+field*4,seed);
        int result=copy(&m,ABI,sizeof(output));
        CHECK(result<=0);
        for(size_t j=0;j<sizeof(output);j++)CHECK(output[j]==0xa5);
    }
    printf("Launch argv: %u checks; bounded real ABI, UTF-8, spaces, malformed pointers and exact limits passed\n",checks);
    return 0;
}
