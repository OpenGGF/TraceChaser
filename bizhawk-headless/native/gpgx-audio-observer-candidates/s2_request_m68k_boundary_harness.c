#include <assert.h>
#include <stdint.h>
#include <string.h>
#include "m68kconf.h"
#include "m68k.h"
#include "cpuhook.h"
#include "audio_trace.h"

enum {
  SELFTEST_TRACE_OK = 0,
  SELFTEST_TRACE_INVALID_PHASE = -2,
  SELFTEST_TRACE_LIMIT = -3
};

uint8_t zram[0x2000];
uint8_t work_ram[0x10000];
static uint8_t page0[0x10000];
static unsigned int fixed_s2_callback_hits;

int vdp_68k_irq_ack(int int_level)
{
  return int_level;
}

static unsigned int fixed_s2_callback(hook_type_t type, int width,
  unsigned int address, unsigned int value)
{
  uint32_t count, ordinal, overflow;
  (void)width;
  if (type == HOOK_M68K_E && address == 0x10d6u)
  {
    assert(gpgx_audio_trace_event_count(&count, &overflow)
      == SELFTEST_TRACE_INVALID_PHASE);
    assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
      == SELFTEST_TRACE_OK);
    assert(ordinal == 0);
    fixed_s2_callback_hits++;
  }
  return value;
}

static void put_byte(unsigned int address, uint8_t value)
{
  page0[address ^ 1u] = value;
}

int main(void)
{
  struct gpgx_audio_trace_config_v1 config;
  struct gpgx_audio_service_kind_v1 kind;
  struct gpgx_audio_service_hook_v1 hook;
  struct gpgx_audio_snapshot_range_v1 range;
  struct gpgx_audio_trace_event events[2];
  uint8_t mask[8192];
  const uint8_t opcode[4] = { 0x13, 0x80, 0x10, 0x09 };
  uint32_t count;
  unsigned int i;

  memset(&config, 0, sizeof(config));
  memset(&kind, 0, sizeof(kind));
  memset(&hook, 0, sizeof(hook));
  memset(&range, 0, sizeof(range));
  memset(mask, 0, sizeof(mask));
  memset(page0, 0, sizeof(page0));
  config.magic = 0x31544147;
  config.abi_version = 4;
  config.struct_size = 64;
  config.kind_size = 16;
  config.hook_size = 32;
  config.range_size = 16;
  config.event_size = 32;
  config.max_depth = 8;
  config.max_opcode_bytes = 8;
  config.reset_service_kind = 1;
  config.watch_mask_bytes = 8192;
  config.kind_count = 1;
  config.hook_count = 1;
  config.range_count = 1;
  config.snapshot_bytes_total = 1;
  config.event_capacity = 65536;
  config.max_service_tokens_per_frame = 65535;
  kind.kind_id = 1;
  kind.flags = 4;
  kind.cancellation_range_count = 1;
  range.range_id = 1;
  range.length = 1;
  hook.hook_token = 24;
  hook.action = 7;
  hook.cpu = 2;
  hook.pc = 0x10d6;
  hook.opcode_length = 4;
  memcpy(hook.opcode, opcode, sizeof(opcode));

  for (i = 0; i < sizeof(opcode); i++)
    put_byte(0x10d6u + i, opcode[i]);
  m68k.memory_map[0].base = page0;
  cpu_hook = fixed_s2_callback;
  assert(gpgx_audio_trace_configure(&config, mask, &kind, &hook, &range)
    == SELFTEST_TRACE_OK);
  m68k_init();
  m68k_set_reg(M68K_REG_PC, 0x10d6);
  m68k_set_reg(M68K_REG_A7, 0x89abcdefu);
  m68k.pref_addr = ~0u;
  m68k.cycles = 0;
  m68k.refresh_cycles = 1000000;
  assert(gpgx_audio_trace_begin_frame() == SELFTEST_TRACE_OK);
  m68k_run(8);
  assert(fixed_s2_callback_hits == 1);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&count)
    == SELFTEST_TRACE_LIMIT);
  assert(gpgx_audio_trace_end_frame() == SELFTEST_TRACE_OK);
  assert(gpgx_audio_trace_drain(events, 2, &count) == SELFTEST_TRACE_OK);
  assert(count == 1);
  assert(events[0].ordinal == 0);
  assert(events[0].kind == 10);
  assert(events[0].pc == 0x10d6);
  assert(events[0].subject == 24);
  assert(events[0].value == 3);
  assert(events[0].source_cpu == 2);
  assert(m68k_get_reg(M68K_REG_A7) == 0x89abcdefu);
  assert(gpgx_audio_trace_disable() == SELFTEST_TRACE_OK);
  cpu_hook = NULL;
  return 0;
}
