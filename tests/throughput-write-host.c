#define _POSIX_C_SOURCE 200809L
#include "dnd_contiguous_write.h"
#include <stdio.h>
#include <stdlib.h>
#include <fcntl.h>
#include <unistd.h>
#include <time.h>
#include <stdint.h>
/* Host filesystem diagnostic only: no claim about GC Loader/SD performance. */
static uint64_t checksum;
static long us(void){struct timespec t;clock_gettime(CLOCK_MONOTONIC,&t);return t.tv_sec*1000000L+t.tv_nsec/1000;}
static long measure(const char *path,size_t chunk){
 int fd=open(path,O_CREAT|O_EXCL|O_RDWR,0600);if(fd<0)exit(2);
 DndContiguousWriter w={.fd=fd,.active=true,.expected=32*1024*1024,.checksum=UINT64_C(14695981039346656037)};
 unsigned char data[16384];for(size_t i=0;i<sizeof(data);i++)data[i]=(unsigned char)(i%256);
 long start=us();for(size_t n=0;n<w.expected;n+=chunk)if(dnd_contiguous_write(&w,data,chunk))exit(3);
 if(fsync(fd))exit(4);long elapsed=us()-start;
 if(checksum && checksum!=w.checksum)exit(6);checksum=w.checksum;if(close(fd)||unlink(path))exit(5);return elapsed;
}
int main(int argc,char **argv){if(argc!=2)return 1;for(int i=0;i<9;i++){
 long before,after;if(i%2){after=measure(argv[1],16384);before=measure(argv[1],4096);}else{before=measure(argv[1],4096);after=measure(argv[1],16384);}printf("%ld %ld\n",before,after);
}return 0;}
