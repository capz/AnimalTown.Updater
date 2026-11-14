/* Execute the real managed console and controller calls without native GX/IPL
 * hardware. These bounded stubs deliberately perform no managed allocation. */
#include "dnd_draw.h"
#include "dnd_async.h"
#include <assert.h>
static bool frame;
int dnd_device_begin(float r,float g,float b,float a){(void)r;(void)g;(void)b;(void)a;assert(!frame);frame=true;return 0;}
int dnd_device_end(void){assert(frame);frame=false;return 0;}
int dnd_device_width(void){return 640;}
int dnd_device_height(void){return 480;}
int dnd_device_display_stat(int kind){(void)kind;return 0;}
int dnd_render_scene(DndArray *settings){(void)settings;return 0;}
int dnd_device_fence_completed(int token){(void)token;return 1;}
void dnd_device_shutdown(void){}
int dnd_texture_ipl_font(DndArray *metadata,int linear){
    (void)linear;assert(metadata&&metadata->element_size==4&&metadata->length==483);
    int *m=(int*)metadata->data;m[0]=16;m[1]=24;m[4]=24;
    for(int i=0;i<95;i++){int at=8+i*5;m[at]=0;m[at+1]=0;m[at+2]=12;m[at+3]=24;m[at+4]=12;}
    return 1;
}
int dnd_texture_dimension(int token,int axis){(void)token;(void)axis;return 256;}
int dnd_draw_release(int token){(void)token;return 0;}
int dnd_canvas_create(int capacity){assert(capacity==8192);return 2;}
int dnd_canvas_layout(int token,float width,float height){(void)token;(void)width;(void)height;return 0;}
int dnd_canvas_viewport(int token,int x,int y,int width,int height){(void)token;(void)x;(void)y;(void)width;(void)height;return 0;}
int dnd_canvas_begin(int token,int width,int height){(void)token;(void)width;(void)height;return 0;}
int dnd_canvas_end(int token){(void)token;return 0;}
int dnd_canvas_vertices(int token,int texture,DndArray *vertices,int count,uint32_t color){(void)token;(void)texture;(void)vertices;(void)count;(void)color;return 0;}
int dnd_canvas_rectangle(int token,float x,float y,float width,float height,uint32_t color){(void)token;(void)x;(void)y;(void)width;(void)height;(void)color;return 0;}
int dnd_canvas_effect(int token,int effect){(void)token;(void)effect;return 0;}
float dnd_draw_cos(float x){return cosf(x);}
float dnd_draw_sin(float x){return sinf(x);}
void dnd_input_poll(void){}
void dnd_input_shutdown(void){}
