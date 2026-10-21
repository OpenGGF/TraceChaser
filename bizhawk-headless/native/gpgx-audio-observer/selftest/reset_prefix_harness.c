/* Bounded real-M68K observer controls; native-reset-prefix-20261021.
 * Synthetic bus/profile is explicit, not evidence of managed constructor execution. */
#include <assert.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include "m68kconf.h"
#include "m68k.h"
#include "reset_prefix.h"
uint8 work_ram[0x10000],zram[0x2000],system_hw=SYSTEM_MD,vdp_pal,region_code=0x80,io_reg[16],zstate;
uint16 lines_per_frame=262;
uint32 zbank;
external_t ext;
t_input input;
T_SRAM sram;
struct config_t config;
Z80_Regs Z80;
static uint8 page[65536];
int vdp_68k_irq_ack(int level) { return level; }
extern uint32_t gpgx_reset_prefix_abi_version(void);
extern int gpgx_reset_prefix_enable(uint32_t);
extern int gpgx_reset_prefix_layout(uint32_t *,uint32_t);
extern int gpgx_reset_prefix_read(uint32_t *,uint32_t,uint32_t *,uint32_t *);
extern int gpgx_reset_prefix_disable(void);
static void byte(unsigned int a,uint8 v) { page[a^1]=v; }
static unsigned int read_status(unsigned int a) { assert(a==0xc00004);return 0x3400; }
static void init(void)
{
  memset(&m68k,0,sizeof(m68k)); memset(page,0,sizeof(page));
  m68k.memory_map[0].base=page; m68k.memory_map[0xc0].read16=read_status;
  /* SSP0000FF00 PC00000100. move.w ($C00004).l,D0 then NOPs. */
  byte(2,0xff);byte(6,1); byte(0x100,0x30);byte(0x101,0x39);
  byte(0x102,0);byte(0x103,0xc0);byte(0x104,0);byte(0x105,4);
  for(unsigned int a=0x106;a<0x200;a+=2) {byte(a,0x4e);byte(a+1,0x71);}
  cart.romsize=65536;io_reg[0]=region_code;
  m68k_init();gpgx_reset_prefix_init_begin();
  m68k.cycles=(((262-192+159)*3420+1004)/7)*7;Z80.cycles=(m68k.cycles/15)*15;
  m68k_pulse_reset();gpgx_reset_prefix_init_end();
}
int main(void)
{
  uint32_t p[128*40],count,done,layout[4];unsigned int origin;
  assert(gpgx_reset_prefix_abi_version()==1);
  assert(gpgx_reset_prefix_layout(layout,4)==0 && layout[0]==160 && layout[1]==128);
  init();origin=m68k.cycles;
  assert(gpgx_reset_prefix_enable(99)==-2);assert(gpgx_reset_prefix_enable(1)==0);
  assert(gpgx_reset_prefix_read(p,128*40,&count,&done)==0 && count==3 && done==0);
  assert(p[3]+280==p[40+3] && p[40+39]==280);
  assert(p[40+4]==((origin/896)+1)*896 && p[40+2]==0x100 && p[40+29]==0xff00);
  m68k_run(origin+112);
  assert(gpgx_reset_prefix_read(p,128*40,&count,&done)==0 && count==6 && done==1);
  assert(p[3*40]==4 && p[3*40+2]==0x100 && p[3*40+30]==0x3039);
  assert(p[4*40]==6 && p[4*40+10]==0xc00004 && p[4*40+11]==0x3400 && p[4*40+12]==2);
  assert(p[5*40]==5 && p[5*40+14]==0x3400 && p[5*40+30]==0x3039);
  for(uint32_t i=0;i<count;i++) assert(p[i*40+1]==i);
  /* Read capacity is sticky, never truncates a valid ordered prefix. */
  assert(gpgx_reset_prefix_read(p,1,&count,&done)==-3);
  assert(gpgx_reset_prefix_read(p,128*40,&count,&done)==-3);
  assert(gpgx_reset_prefix_disable()==0);assert(gpgx_reset_prefix_enable(1)==-3);
  init();m68k_run(m68k.cycles+112);assert(gpgx_reset_prefix_enable(1)==-4); /* late arm */
  init();assert(gpgx_reset_prefix_enable(1)==0);m68k_pulse_reset();
  assert(gpgx_reset_prefix_read(p,128*40,&count,&done)==-4); /* genuine reset invalidates */
  init();assert(gpgx_reset_prefix_enable(1)==0);m68k.cycles++;
  /* Native pre-arm discontinuity guard. Managed host generation also catches equal-state loads. */
  assert(gpgx_reset_prefix_enable(1)==-4);
  init();assert(gpgx_reset_prefix_enable(1)==0);
  gpgx_reset_prefix_fetch();gpgx_reset_prefix_bus(0xa11100,0,2,0);
  assert(gpgx_reset_prefix_read(p,128*40,&count,&done)==-4); /* unsupported producer */
  init();assert(gpgx_reset_prefix_enable(1)==0);
  for(unsigned int i=0;i<128;i++) {gpgx_reset_prefix_fetch();gpgx_reset_prefix_retire();}
  assert(gpgx_reset_prefix_read(p,128*40,&count,&done)==-3); /* record capacity, no order loss */
  puts("reset-prefix-harness: real M68K reset/vector/clock/order, capacity/reset/late-arm/unsupported controls pass");
  return 0;
}
