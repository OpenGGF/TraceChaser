#include <assert.h>
#include <stdint.h>
#include <string.h>
#include "shared.h"

struct selftest_m68k m68k;
uint8_t zram[0x2000];
uint8_t work_ram[0x10000];
uint32_t selftest_m68k_a7;
#include "audio_trace.c"

static struct gpgx_audio_trace_config_v1 config;
static struct gpgx_audio_service_kind_v1 kind;
static struct gpgx_audio_service_hook_v1 hooks[2];
static struct gpgx_audio_snapshot_range_v1 range;
static uint8_t mask[8192];

static void fixture(void)
{
  gpgx_audio_trace_disable();
  memset(&config, 0, sizeof(config));
  memset(&kind, 0, sizeof(kind));
  memset(hooks, 0, sizeof(hooks));
  memset(&range, 0, sizeof(range));
  memset(mask, 0, sizeof(mask));
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
  kind.flags = KIND_ALLOW_CHILDREN;
  kind.cancellation_range_count = 1;
  range.range_id = 1;
  range.length = 1;
  hooks[0].hook_token = 24;
  hooks[0].action = ACTION_OBSERVATION_MARKER;
  hooks[0].cpu = GPGX_AUDIO_TRACE_CPU_M68K;
  hooks[0].pc = 0x10d6;
  hooks[0].opcode_length = 4;
  hooks[0].opcode[0] = 0x13;
  hooks[0].opcode[1] = 0x80;
  hooks[0].opcode[2] = 0x10;
  hooks[0].opcode[3] = 0x09;
}

static int configure(void)
{
  return gpgx_audio_trace_configure(&config, mask, &kind, hooks, &range);
}

static void fixed_successor_boundary(void)
{
  struct gpgx_audio_trace_event *ordinary;
  uint8_t rom[65536];
  uint8_t previous;
  uint32_t i, ordinal, count, overflow;
  const uint8_t opcode[4] = { 0x13, 0x80, 0x10, 0x09 };

  memset(rom, 0, sizeof(rom));
  memset(&m68k, 0, sizeof(m68k));
  for (i = 0; i < sizeof(opcode); i++)
    rom[((0x10d6u + i) & 0xffffu) ^ 1u] = opcode[i];
  m68k.memory_map[0].base = rom;

  fixture();
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_INVALID_PHASE);
  assert(configure() == TRACE_OK);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_INVALID_PHASE);
  assert(gpgx_audio_trace_begin_frame() == TRACE_OK);
  assert(gpgx_audio_trace_event_count(&count, &overflow)
    == TRACE_INVALID_PHASE);
  previous = gpgx_audio_trace_enter_cpu(GPGX_AUDIO_TRACE_CPU_M68K);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_ABI_OR_CONFIG_LIMIT);
  gpgx_audio_trace_s2_request_callback_begin(0x10d8);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_ABI_OR_CONFIG_LIMIT);
  gpgx_audio_trace_s2_request_callback_end();

  gpgx_audio_trace_s2_request_callback_begin(0x10d6);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(NULL)
    == TRACE_INVALID_ARGUMENT);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) == TRACE_OK
    && ordinal == 0);
  ordinary = new_event(EVENT_HOOK_MARKER);
  assert(ordinary && ordinary->ordinal == 0);
  ordinary->pc = 0x2000;
  ordinary->subject = 25;
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) == TRACE_OK
    && ordinal == 1);
  trace_depth = 1;
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_ABI_OR_CONFIG_LIMIT);
  trace_depth = 0;
  trace_deferred_begin.pending = 1;
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_ABI_OR_CONFIG_LIMIT);
  memset(&trace_deferred_begin, 0, sizeof(trace_deferred_begin));
  gpgx_audio_trace_s2_request_callback_end();
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_ABI_OR_CONFIG_LIMIT);
  gpgx_audio_trace_instruction(GPGX_AUDIO_TRACE_CPU_M68K, 0x10d6);
  assert(trace_event_count_value == 2);
  assert(trace_events[1].ordinal == 1);
  assert(trace_events[1].kind == EVENT_HOOK_MARKER);
  assert(trace_events[1].subject == 24);
  assert(trace_events[1].value == 3);
  gpgx_audio_trace_leave_cpu(previous);
  assert(gpgx_audio_trace_end_frame() == TRACE_OK);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_INVALID_PHASE);
  assert(gpgx_audio_trace_abort_frame() == TRACE_OK);

  assert(gpgx_audio_trace_begin_frame() == TRACE_OK);
  previous = gpgx_audio_trace_enter_cpu(GPGX_AUDIO_TRACE_CPU_M68K);
  gpgx_audio_trace_s2_request_callback_begin(0x10d6);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) == TRACE_OK);
  assert(gpgx_audio_trace_abort_frame() == TRACE_OK);
  assert(!trace_s2_request_callback_active);
  gpgx_audio_trace_leave_cpu(previous);

  fixture();
  hooks[0].hook_token = 23;
  assert(configure() == TRACE_OK);
  assert(gpgx_audio_trace_begin_frame() == TRACE_OK);
  previous = gpgx_audio_trace_enter_cpu(GPGX_AUDIO_TRACE_CPU_M68K);
  gpgx_audio_trace_s2_request_callback_begin(0x10d6);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal)
    == TRACE_ABI_OR_CONFIG_LIMIT);
  gpgx_audio_trace_s2_request_callback_end();
  gpgx_audio_trace_leave_cpu(previous);
  assert(gpgx_audio_trace_abort_frame() == TRACE_OK);
  assert(gpgx_audio_trace_disable() == TRACE_OK);
}

int main(void)
{
  fixed_successor_boundary();
  return 0;
}
