/* Stall ABI v2 contract (0005-m68k-cpu-ledger-v2.patch): layout, fail-closed reads, per-PC accounting with inside
   stalls excluded, VInt-to-VInt bank rotation with straddle/carry handoff, interrupt vs exception frame context,
   synthetic RTE, ring/bank capacity failures, and discontinuity/reset detection. Core call sites are reproduced in
   the order 0005 places them: fetch, refresh, begin, handler (classified stalls), USE_CYCLES, end. */
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

uint32_t gpgx_m68k_stall2_abi_version(void);
int32_t gpgx_m68k_stall2_layout(uint32_t *out, uint32_t count);
int32_t gpgx_m68k_stall2_enable(uint32_t abi_version);
int32_t gpgx_m68k_stall2_read_frame(struct gpgx_m68k_stall2_frame *out, uint32_t size);
int32_t gpgx_m68k_stall2_take_accepts(struct gpgx_m68k_stall2_accept *out, uint32_t size, uint32_t capacity,
  uint32_t *count);
int32_t gpgx_m68k_stall2_take_bank(struct gpgx_m68k_stall2_bank *header, uint32_t header_size,
  struct gpgx_m68k_stall2_slot *slots, uint32_t slot_size, uint32_t capacity, uint32_t *available);
int32_t gpgx_m68k_stall2_disable(void);

static struct gpgx_m68k_stall2_slot slots[GPGX_M68K_STALL2_MAX_LOAD];
static struct gpgx_m68k_stall2_accept ring[GPGX_M68K_STALL2_RING];
static uint32_t sp = 0xFFFE00;

/* one instruction: base cycles plus an optional classified stall inside it */
static void instruction(uint32_t pc, uint32_t ir, uint32_t base, uint32_t inside_kind, uint32_t inside_mclk)
{
  gpgx_m68k_stall2_fetch(pc);
  gpgx_m68k_stall2_begin(ir, sp);
  if (inside_mclk) { m68k.cycles += inside_mclk; gpgx_m68k_stall2_classify(inside_kind, inside_mclk); }
  m68k.cycles += base * 7;
  gpgx_m68k_stall2_end();
}

/* interrupt accepted between instructions (m68k_run entry): frame push, then the acceptance cost */
static void accept_between(uint32_t level, uint32_t return_pc)
{
  uint32_t index = (m68k.cycles / 7) % 10, cost = (index ? 60 - index : 50) * 7;
  gpgx_m68k_stall2_note_return(return_pc);
  sp -= 6;
  gpgx_m68k_stall2_frame_pushed(sp);
  gpgx_m68k_stall2_accept(level, index, cost);
  m68k.cycles += cost;
}

static void handler_return(uint32_t pc)
{
  instruction(pc, 0x4E73, 20, 0, 0);   /* RTE at the frame the acceptance pushed */
  sp += 6;
}

static uint32_t find(const struct gpgx_m68k_stall2_slot *s, uint32_t n, uint32_t pc, uint32_t context, uint32_t *mclk)
{
  uint32_t i;
  for (i = 0; i < n; i++)
    if (s[i].key == ((pc & 0xFFFFFFu) | (context << 24) | 0x80000000u)) { *mclk = s[i].mclk; return s[i].count; }
  *mclk = 0;
  return 0;
}

static int32_t take(struct gpgx_m68k_stall2_bank *bank, uint32_t *available)
{
  return gpgx_m68k_stall2_take_bank(bank, sizeof(*bank), slots, sizeof(slots[0]), GPGX_M68K_STALL2_MAX_LOAD, available);
}

int main(void)
{
  struct gpgx_m68k_stall2_frame frame;
  struct gpgx_m68k_stall2_bank bank;
  uint32_t layout[8], available, count, mclk, i;

  assert(gpgx_m68k_stall2_abi_version() == 2u);
  assert(gpgx_m68k_stall2_layout(layout, 7) == -2);
  assert(gpgx_m68k_stall2_layout(layout, 8) == 0);
  assert(layout[0] == 12 && layout[1] == 32 && layout[2] == 176 && layout[3] == 40);
  assert(layout[4] == GPGX_M68K_STALL2_SLOTS && layout[6] == 3 && layout[7] == 512);

  /* fail closed before enable and on size mismatches */
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame)) == -1);
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame) - 1) == -2);
  assert(take(&bank, &available) == -1);
  assert(gpgx_m68k_stall2_enable(1) == -2);
  assert(!gpgx_m68k_stall2_enabled);

  m68k.cycles = 7000;
  assert(gpgx_m68k_stall2_enable(2) == 0);
  /* bank 0 (opened by enable) */
  instruction(0x1000, 0x4E71, 4, 0, 0);
  accept_between(6, 0x1002);                      /* closes bank 0 */
  assert(take(&bank, &available) == 0 && available == 1);
  assert(bank.sequence == 0 && bank.opened_by_vint == 0 && bank.open_accept_ordinal == 0xFFFFFFFFu);
  assert(take(&bank, &available) == 0 && available == 0);

  /* bank 1: handler in interrupt context with a DMA freeze inside one instruction, RTE, then main code */
  instruction(0xFFFFF0, 0x4EF9, 12, 0, 0);       /* jmp in RAM, interrupt context */
  instruction(0x0584, 0x33FC, 20, GPGX_M68K_STALL2_DMA, 3218);
  handler_return(0x0600);                         /* RTE matching the pushed frame */
  instruction(0x1002, 0x4E71, 4, GPGX_M68K_STALL2_Z80_AREA, 7);
  gpgx_m68k_stall2_fetch(0x1004);                 /* refresh occurs after fetch, before begin */
  m68k.cycles += 14;
  gpgx_m68k_stall2_refresh();
  gpgx_m68k_stall2_begin(0x4E71, sp);
  m68k.cycles += 4 * 7;
  gpgx_m68k_stall2_end();
  m68k.cycles += 73;                              /* a Z80 bus access between instructions */
  gpgx_m68k_stall2_classify(GPGX_M68K_STALL2_Z80_BUS, 73);
  /* an instruction in flight when the next VInt is accepted inside it (SR change) */
  gpgx_m68k_stall2_fetch(0x1006);
  gpgx_m68k_stall2_begin(0x46FC, sp);
  m68k.cycles += 7 * 7;                           /* part of its time before the acceptance */
  {
    uint32_t index = (m68k.cycles / 7) % 10, cost = (index ? 60 - index : 50) * 7;
    gpgx_m68k_stall2_note_return(0x100A);
    sp -= 6;
    gpgx_m68k_stall2_frame_pushed(sp);
    gpgx_m68k_stall2_accept(6, index, cost);
    m68k.cycles += cost;
  }
  m68k.cycles += 13 * 7;                          /* the rest of the instruction after the acceptance */
  gpgx_m68k_stall2_end();
  assert(take(&bank, &available) == 0 && available == 1);
  assert(bank.sequence == 1 && bank.opened_by_vint == 1 && bank.open_accept_ordinal == 0 && bank.close_accept_ordinal == 1);
  assert(find(slots, bank.entries, 0xFFFFF0, 1, &mclk) == 1 && mclk == 12 * 7);
  assert(find(slots, bank.entries, 0x0584, 1, &mclk) == 1 && mclk == 20 * 7);   /* DMA excluded */
  assert(find(slots, bank.entries, 0x0600, 1, &mclk) == 1 && mclk == 20 * 7);   /* RTE counted in interrupt context */
  assert(find(slots, bank.entries, 0x1002, 0, &mclk) == 1 && mclk == 4 * 7);    /* Z80-area latency excluded */
  assert(find(slots, bank.entries, 0x1004, 0, &mclk) == 1 && mclk == 4 * 7);    /* refresh excluded */
  assert(find(slots, bank.entries, 0x1006, 0, &mclk) == 1 && mclk == 7 * 7);    /* straddle: time before acceptance */
  assert(bank.straddle_pc == 0x1006 && bank.straddle_mclk == 7 * 7);
  assert(bank.refresh_events == 1 && bank.kind_mclk[GPGX_M68K_STALL2_DMA] == 3218);
  assert(bank.kind_mclk[GPGX_M68K_STALL2_Z80_AREA] == 7 && bank.kind_mclk[GPGX_M68K_STALL2_Z80_BUS] == 73);
  assert(bank.accept_counts[0] == 1 && bank.synthetic_rte == 0);
  /* identity: interval = slot MCLK + carry + classified + refresh, with the opening acceptance cost inside */
  {
    uint64_t sum = 0;
    for (i = 0; i < bank.entries; i++) sum += slots[i].mclk;
    sum += bank.carry_mclk + 14ull * bank.refresh_events;
    for (i = 0; i < GPGX_M68K_STALL2_KINDS; i++)
      sum += i == GPGX_M68K_STALL2_DMA_REWIND ? 0 : bank.kind_mclk[i];
    assert(sum == bank.close_stamp_mclk - bank.open_stamp_mclk);
  }

  /* bank 2 holds the carried remainder of the straddling instruction and the second acceptance cost */
  handler_return(0x0610);
  accept_between(6, 0x100A);
  assert(take(&bank, &available) == 0 && available == 1);
  assert(bank.sequence == 2 && bank.carry_pc == 0x1006 && bank.carry_mclk == 13 * 7);
  assert(bank.open_accept_ordinal == 1 && bank.close_accept_ordinal == 2);

  /* synthetic RTE: no tracked frame at this SP keeps the context unchanged */
  handler_return(0x0620);
  instruction(0x2000, 0x4E73, 20, 0, 0);
  accept_between(6, 0x2002);
  assert(take(&bank, &available) == 0 && available == 1 && bank.synthetic_rte >= 1);

  /* TRAP-style frame (not announced as an interrupt) does not switch context, and its RTE pops it */
  handler_return(0x0630);
  sp -= 6;
  gpgx_m68k_stall2_frame_pushed(sp);
  instruction(0x3000, 0x4E71, 4, 0, 0);
  handler_return(0x3002);
  instruction(0x3004, 0x4E71, 4, 0, 0);
  accept_between(6, 0x3006);
  assert(take(&bank, &available) == 0 && available == 1);
  assert(find(slots, bank.entries, 0x3000, 0, &mclk) == 1 && find(slots, bank.entries, 0x3004, 0, &mclk) == 1);
  assert(find(slots, bank.entries, 0x3002, 0, &mclk) == 1 && bank.synthetic_rte == 0);

  /* ring drains in order */
  assert(gpgx_m68k_stall2_take_accepts(ring, sizeof(ring[0]), GPGX_M68K_STALL2_RING, &count) == 0);
  assert(count == 5 && ring[0].level == 6 && ring[1].inside_instruction == 1 && ring[1].return_pc == 0x100A);
  assert(ring[0].inside_instruction == 0 && ring[1].closes_bank_sequence == 1);
  for (i = 1; i < count; i++) assert(ring[i].ordinal == ring[i - 1].ordinal + 1 && ring[i].stamp_mclk > ring[i - 1].stamp_mclk);

  /* three unread closed intervals overrun the banks: fail closed */
  accept_between(6, 0x4000);
  accept_between(6, 0x4000);
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame)) == 0 && frame.closed_banks == 2 && frame.flags == 0);
  accept_between(6, 0x4000);
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame)) == 0 && (frame.flags & GPGX_M68K_STALL2_FLAG_BANK_OVERRUN));
  assert(take(&bank, &available) == -3);

  /* ring overflow: more acceptances than the ring holds between reads */
  m68k.cycles = 7000;
  assert(gpgx_m68k_stall2_enable(2) == 0);
  for (i = 0; i < GPGX_M68K_STALL2_RING + 1; i++) accept_between(4, 0x5000);
  assert(gpgx_m68k_stall2_take_accepts(ring, sizeof(ring[0]), GPGX_M68K_STALL2_RING, &count) == -3);

  /* discontinuity: the CPU timing state changed between frames (loadstate) */
  assert(gpgx_m68k_stall2_enable(2) == 0);
  gpgx_m68k_stall2_frame_end(896040, 100, 900, 0x1D1C, 50);
  gpgx_m68k_stall2_frame_begin(100, 900, 0x1D1C, 50);
  assert(take(&bank, &available) == 0);
  gpgx_m68k_stall2_frame_end(896040, 120, 900, 0x1D1C, 50);
  gpgx_m68k_stall2_frame_begin(121, 900, 0x1D1C, 50);
  assert(take(&bank, &available) == -4);
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame)) == 0 && (frame.flags & GPGX_M68K_STALL2_FLAG_DISCONTINUITY));

  /* reset and bus error are discontinuities too; accounting misuse is a capacity-class failure */
  assert(gpgx_m68k_stall2_enable(2) == 0);
  gpgx_m68k_stall2_reset();
  assert(take(&bank, &available) == -4);
  assert(gpgx_m68k_stall2_enable(2) == 0);
  gpgx_m68k_stall2_bus_error();
  assert(take(&bank, &available) == -4);
  assert(gpgx_m68k_stall2_enable(2) == 0);
  gpgx_m68k_stall2_end();
  assert(take(&bank, &available) == -3);

  /* m68k_set_irq_delay, in 0005's call order: the outer VDP-control MOVE is finished early (USE_CYCLES then
     inline_close), the next instruction runs inline (fetch, inline_begin, handler), a VInt is accepted inside it by
     the trailing m68ki_check_interrupts, the outer handler's remaining work runs, then the loop charges the inline
     instruction's CYC_INSTRUCTION and calls end. GPGX charges that base after the acceptance, so it is carry. */
  m68k.cycles = 70000;
  sp = 0xFFFE00;
  assert(gpgx_m68k_stall2_enable(2) == 0);
  instruction(0x6FFC, 0x4E71, 4, 0, 0);
  accept_between(6, 0x6FFE);                       /* closes the partial bank 0 */
  handler_return(0x0640);
  assert(take(&bank, &available) == 0 && available == 1 && bank.sequence == 0);
  gpgx_m68k_stall2_fetch(0x7000);
  gpgx_m68k_stall2_begin(0x33C0, sp);              /* move.w d0,(VDP control).l */
  m68k.cycles += 8 * 7;                            /* handler work up to the VDP write */
  m68k.cycles += 12 * 7;                           /* USE_CYCLES(CYC_INSTRUCTION[outer]) inside set_irq_delay */
  gpgx_m68k_stall2_inline_close();
  gpgx_m68k_stall2_fetch(0x7006);
  gpgx_m68k_stall2_inline_begin(0x4E71, sp);
  m68k.cycles += 6 * 7;                            /* inline handler */
  {
    uint32_t index = (m68k.cycles / 7) % 10, cost = (index ? 60 - index : 50) * 7;
    gpgx_m68k_stall2_note_return(0x7008);
    sp -= 6;
    gpgx_m68k_stall2_frame_pushed(sp);
    gpgx_m68k_stall2_accept(6, index, cost);       /* inside the inline instruction */
    m68k.cycles += cost;
  }
  m68k.cycles += 2 * 7;                            /* outer handler work after the VDP write */
  m68k.cycles += 4 * 7;                            /* loop USE_CYCLES(CYC_INSTRUCTION[inline]) */
  gpgx_m68k_stall2_end();
  assert(take(&bank, &available) == 0 && available == 1 && bank.sequence == 1);
  assert(bank.irq_delay_inline == 1);
  assert(find(slots, bank.entries, 0x7000, 0, &mclk) == 1 && mclk == (8 + 12) * 7);
  assert(find(slots, bank.entries, 0x7006, 0, &mclk) == 1 && mclk == 6 * 7);
  assert(bank.straddle_pc == 0x7006 && bank.straddle_mclk == 6 * 7);
  handler_return(0x0650);
  accept_between(6, 0x700A);
  assert(take(&bank, &available) == 0 && available == 1 && bank.sequence == 2);
  assert(bank.carry_pc == 0x7006 && bank.carry_mclk == (2 + 4) * 7);
  assert(gpgx_m68k_stall2_take_accepts(ring, sizeof(ring[0]), GPGX_M68K_STALL2_RING, &count) == 0);
  assert(count == 3 && ring[1].inside_instruction == 1 && ring[1].return_pc == 0x7008 && ring[1].closes_bank_sequence == 1);

  /* an inline close outside any instruction (no active begin) is an accounting failure */
  assert(gpgx_m68k_stall2_enable(2) == 0);
  gpgx_m68k_stall2_inline_close();
  assert(take(&bank, &available) == -3);

  /* the MD cartridge path only: a Sega CD frame fails the session closed */
  assert(gpgx_m68k_stall2_enable(2) == 0);
  gpgx_m68k_stall2_unsupported();
  assert(take(&bank, &available) == -4);
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame)) == 0 && (frame.flags & GPGX_M68K_STALL2_FLAG_UNSUPPORTED));

  assert(gpgx_m68k_stall2_disable() == 0);
  assert(gpgx_m68k_stall2_read_frame(&frame, sizeof(frame)) == -1);
  puts("m68k-stall2-harness: ok");
  return 0;
}
