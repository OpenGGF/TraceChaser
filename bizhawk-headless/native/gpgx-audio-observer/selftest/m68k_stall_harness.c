/* Stall ABI v1 contract (0004-m68k-stall-counters.patch): exports, fail-closed reads, enable/reset, gated call sites,
   and the monotonic frame-relative stamp. The core-side call sites are reproduced here exactly as 0004 writes them:
   each is guarded by gpgx_m68k_stall_enabled, and none writes emulated state. */
#include <assert.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include "shared.h"
#include "audio_trace.h"

struct selftest_m68k m68k;
uint8_t zram[0x2000];
uint8_t work_ram[0x10000];
uint32_t selftest_m68k_a7;

/* 0004 defines these ECL_EXPORT controls in audio_trace.c without header prototypes. */
uint32_t gpgx_m68k_stall_abi_version(void);
uint32_t gpgx_m68k_stall_snapshot_size(void);
int32_t gpgx_m68k_stall_enable(uint32_t abi_version);
int32_t gpgx_m68k_stall_read(struct gpgx_m68k_stall_snapshot_v1 *out, uint32_t size);
int32_t gpgx_m68k_stall_disable(void);

static void z80_bus_site(uint32_t z80_cycles)
{
  m68k.cycles += (((z80_cycles % 7) + 72) / 7) * 7;
  if (gpgx_m68k_stall_enabled) gpgx_m68k_stall_z80_bus((((z80_cycles % 7) + 72) / 7) * 7);
}

static void refresh_site(void)
{
  m68k.cycles += (2 * 7);
  GPGX_M68K_STALL_COUNT_REFRESH();
}

static void dma_site(uint32_t end)
{
  if (gpgx_m68k_stall_enabled) gpgx_m68k_stall_dma(m68k.cycles, end);
  m68k.cycles = end;
}

static void fifo_site(uint32_t fifo_cycle)
{
  if (gpgx_m68k_stall_enabled) gpgx_m68k_stall_fifo(m68k.cycles, ((fifo_cycle + 6) / 7) * 7);
  m68k.cycles = ((fifo_cycle + 6) / 7) * 7;
}

static void frame_end_site(uint32_t mcycles_vdp)
{
  m68k.cycles -= mcycles_vdp;
  if (gpgx_m68k_stall_enabled) gpgx_m68k_stall_frame_wrap(mcycles_vdp);
}

static uint64_t stamp(const struct gpgx_m68k_stall_snapshot_v1 *s)
{
  return s->frame_base_mclk + s->m68k_cycles;
}

int main(void)
{
  struct gpgx_m68k_stall_snapshot_v1 s;
  uint64_t previous;

  assert(sizeof(struct gpgx_m68k_stall_snapshot_v1) == 80);
  assert(gpgx_m68k_stall_abi_version() == 1u);
  assert(gpgx_m68k_stall_snapshot_size() == 80u);

  /* Unsupported controls fail explicitly; nothing reads as zero before enable. */
  memset(&s, 0xA5, sizeof(s));
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == -1);
  assert(s.abi_version == 0xA5A5A5A5u);
  assert(gpgx_m68k_stall_read(NULL, sizeof(s)) == -2);
  assert(gpgx_m68k_stall_read(&s, sizeof(s) - 1) == -2);
  assert(gpgx_m68k_stall_enable(0) == -1);
  assert(gpgx_m68k_stall_enable(2) == -1);
  assert(!gpgx_m68k_stall_enabled);

  /* Disabled call sites leave the counters untouched but still apply the core's own timing. */
  m68k.cycles = 1000;
  z80_bus_site(3);
  refresh_site();
  dma_site(5000);
  fifo_site(6000);
  assert(m68k.cycles == 6006);
  assert(gpgx_m68k_stall_enable(1) == 0);
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == 0);
  assert(s.abi_version == 1u && s.struct_size == 80u && s.frame_ordinal == 0u && s.frame_base_mclk == 0u);
  assert(s.z80_bus_access_count == 0u && s.z80_bus_mclk == 0u && s.fifo_wait_count == 0u && s.fifo_wait_mclk == 0u);
  assert(s.refresh_events == 0u && s.dma_freeze_count == 0u && s.dma_freeze_mclk == 0u);
  assert(s.m68k_cycles == 6006u);
  previous = stamp(&s);

  /* Enabled call sites record exactly the MCLK the core adds. */
  m68k.cycles = 100;
  z80_bus_site(0);   /* (0+72)/7*7 = 70 */
  z80_bus_site(6);   /* (6+72)/7*7 = 77 */
  refresh_site();
  refresh_site();
  dma_site(3318);    /* 100+70+77+28 = 275 -> 3318: 3043 */
  dma_site(3000);    /* not a freeze forward: not counted */
  fifo_site(3001);   /* (3001+6)/7*7 = 3003, from 3000: 3 */
  fifo_site(3000);   /* 3003 from 3003: not counted */
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == 0);
  assert(s.z80_bus_access_count == 2u && s.z80_bus_mclk == 147u);
  assert(s.refresh_events == 2u);
  assert(s.dma_freeze_count == 1u && s.dma_freeze_mclk == 3043u);
  assert(s.fifo_wait_count == 1u && s.fifo_wait_mclk == 3u);
  assert(s.m68k_cycles == 3003u);

  /* Frame rebase keeps base + m68k_cycles continuous and monotonic. */
  m68k.cycles = 896040u + 20u;
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == 0);
  previous = stamp(&s);
  frame_end_site(896040u);
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == 0);
  assert(s.frame_ordinal == 1u && s.frame_base_mclk == 896040u && s.m68k_cycles == 20u);
  assert(stamp(&s) == previous);
  m68k.cycles = 40u;
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == 0);
  assert(stamp(&s) > previous);

  /* Disable fails reads closed; re-enable resets every counter. */
  assert(gpgx_m68k_stall_disable() == 0);
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == -1);
  z80_bus_site(1);
  assert(gpgx_m68k_stall_enable(1) == 0);
  assert(gpgx_m68k_stall_read(&s, sizeof(s)) == 0);
  assert(s.frame_ordinal == 0u && s.frame_base_mclk == 0u && s.z80_bus_access_count == 0u && s.refresh_events == 0u);
  assert(s.dma_freeze_mclk == 0u && s.fifo_wait_mclk == 0u);
  assert(gpgx_m68k_stall_disable() == 0);

  puts("m68k-stall-harness: ok");
  return 0;
}
