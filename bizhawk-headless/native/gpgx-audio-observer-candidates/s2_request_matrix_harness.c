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
static struct gpgx_audio_service_kind_v1 kinds[2];
static struct gpgx_audio_service_hook_v1 hooks[3];
static struct gpgx_audio_snapshot_range_v1 range;
static uint8_t mask[8192];

static void fixture(void)
{
  gpgx_audio_trace_disable();
  memset(&config, 0, sizeof(config));
  memset(kinds, 0, sizeof(kinds));
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
  config.kind_count = 2;
  config.hook_count = 2;
  config.range_count = 1;
  config.snapshot_bytes_total = 2;
  config.event_capacity = 65536;
  config.max_service_tokens_per_frame = 65535;
  config.max_continuation_frames = 4;
  kinds[0].kind_id = 1;
  kinds[0].flags = KIND_ALLOW_CHILDREN;
  kinds[0].cancellation_range_count = 1;
  kinds[1].kind_id = 3;
  kinds[1].flags = KIND_ALLOW_CONTINUATION | KIND_ALLOW_CHILDREN;
  kinds[1].cancellation_range_count = 1;
  kinds[1].continuation_frame_limit = 4;
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
  hooks[1] = hooks[0];
  hooks[1].hook_token = 25;
  hooks[1].expected_active_kind = 3;
}

static int configure(void)
{
  return gpgx_audio_trace_configure(&config, mask, kinds, hooks, &range);
}

static void install_kind3_topology(uint16_t token, uint16_t parent,
  uint8_t kind, uint8_t depth)
{
  memset(trace_stack, 0, sizeof(trace_stack));
  trace_stack[0].token = token;
  trace_stack[0].parent = parent;
  trace_stack[0].kind = kind;
  trace_stack[0].depth = depth;
  trace_depth = 1;
}

static void clear_test_topology(void)
{
  trace_depth = 0;
  memset(trace_stack, 0, sizeof(trace_stack));
  memset(&trace_deferred_begin, 0, sizeof(trace_deferred_begin));
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
  install_kind3_topology(7, 0, 3, 0);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) == TRACE_OK
    && ordinal == 1);
  clear_test_topology();
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

static void kind3_successor_boundary(void)
{
  struct gpgx_audio_trace_event events[1];
  uint8_t rom[65536];
  uint8_t previous;
  uint32_t i, ordinal, count;
  const uint8_t opcode[4] = { 0x13, 0x80, 0x10, 0x09 };

  memset(rom, 0, sizeof(rom));
  memset(&m68k, 0, sizeof(m68k));
  for (i = 0; i < sizeof(opcode); i++)
    rom[((0x10d6u + i) & 0xffffu) ^ 1u] = opcode[i];
  m68k.memory_map[0].base = rom;

  fixture();
  assert(configure() == TRACE_OK);
  assert(gpgx_audio_trace_begin_frame() == TRACE_OK);
  previous = gpgx_audio_trace_enter_cpu(GPGX_AUDIO_TRACE_CPU_M68K);
  install_kind3_topology(0x2345, 0, 3, 0);
  gpgx_audio_trace_s2_request_callback_begin(0x10d6);
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) == TRACE_OK
    && ordinal == 0);
  gpgx_audio_trace_s2_request_callback_end();
  gpgx_audio_trace_instruction(GPGX_AUDIO_TRACE_CPU_M68K, 0x10d6);
  assert(trace_event_count_value == 1);
  assert(trace_events[0].kind == EVENT_HOOK_MARKER);
  assert(trace_events[0].subject == 25);
  assert(trace_events[0].service_token == 0x2345);
  assert(trace_events[0].parent_token == 0);
  assert(trace_events[0].service_kind == 3);
  assert(trace_events[0].depth == 0);
  gpgx_audio_trace_leave_cpu(previous);
  assert(gpgx_audio_trace_end_frame() == TRACE_OK);
  assert(gpgx_audio_trace_drain(events, 1, &count) == TRACE_OK);
  assert(count == 1);
  assert(events[0].subject == 25 && events[0].service_token == 0x2345);
  assert(gpgx_audio_trace_disable() == TRACE_OK);
}

static void rejects_nonreviewed_topologies(void)
{
  uint8_t previous, depth;
  uint32_t ordinal;

#define BEGIN_CALLBACK(source) do { \
  fixture(); \
  assert(configure() == TRACE_OK); \
  assert(gpgx_audio_trace_begin_frame() == TRACE_OK); \
  previous = gpgx_audio_trace_enter_cpu(source); \
  gpgx_audio_trace_s2_request_callback_begin(0x10d6); \
} while (0)
#define EXPECT_REJECT_AND_END() do { \
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) \
    == TRACE_ABI_OR_CONFIG_LIMIT); \
  gpgx_audio_trace_s2_request_callback_end(); \
  gpgx_audio_trace_leave_cpu(previous); \
  clear_test_topology(); \
  assert(gpgx_audio_trace_abort_frame() == TRACE_OK); \
} while (0)

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_Z80);
  EXPECT_REJECT_AND_END();

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
  install_kind3_topology(0, 0, 3, 0);
  EXPECT_REJECT_AND_END();

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
  install_kind3_topology(1, 1, 3, 0);
  EXPECT_REJECT_AND_END();

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
  install_kind3_topology(1, 0, 2, 0);
  EXPECT_REJECT_AND_END();

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
  install_kind3_topology(1, 0, 3, 1);
  EXPECT_REJECT_AND_END();

  for (depth = 2; depth <= TRACE_MAX_DEPTH; depth++)
  {
    BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
    install_kind3_topology(1, 0, 3, 0);
    trace_depth = depth;
    EXPECT_REJECT_AND_END();
  }

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
  trace_deferred_begin.pending = 1;
  EXPECT_REJECT_AND_END();

  BEGIN_CALLBACK(GPGX_AUDIO_TRACE_CPU_M68K);
  install_kind3_topology(1, 0, 3, 0);
  trace_deferred_begin.pending = 1;
  EXPECT_REJECT_AND_END();

#undef EXPECT_REJECT_AND_END
#undef BEGIN_CALLBACK
}

static void rejects_marker_inventory_mutations(void)
{
  uint8_t previous;
  uint32_t index, ordinal;
  struct gpgx_audio_service_hook_v1 swap;

#define REJECT_INVENTORY_MUTATION(statement) do { \
  fixture(); \
  assert(configure() == TRACE_OK); \
  assert(gpgx_audio_trace_begin_frame() == TRACE_OK); \
  previous = gpgx_audio_trace_enter_cpu(GPGX_AUDIO_TRACE_CPU_M68K); \
  gpgx_audio_trace_s2_request_callback_begin(0x10d6); \
  statement; \
  assert(gpgx_audio_trace_s2_request_successor_ordinal(&ordinal) \
    == TRACE_ABI_OR_CONFIG_LIMIT); \
  gpgx_audio_trace_s2_request_callback_end(); \
  gpgx_audio_trace_leave_cpu(previous); \
  assert(gpgx_audio_trace_abort_frame() == TRACE_OK); \
} while (0)

  REJECT_INVENTORY_MUTATION(trace_config.hook_count = 1);
  REJECT_INVENTORY_MUTATION(
    trace_config.hook_count = 3;
    trace_hooks[2] = trace_hooks[1];
    trace_hooks[2].hook_token = 26;
    trace_hooks[2].expected_active_kind = 1);
  REJECT_INVENTORY_MUTATION(
    swap = trace_hooks[0];
    trace_hooks[0] = trace_hooks[1];
    trace_hooks[1] = swap);
  for (index = 0; index < 2; index++)
  {
    REJECT_INVENTORY_MUTATION(trace_hooks[index].hook_token++);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].action = ACTION_PUSH_BEGIN);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].cpu = GPGX_AUDIO_TRACE_CPU_Z80);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].pc++);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].service_kind = 1);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].expected_active_kind =
      index ? 0 : 3);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].flags = 1);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].opcode_length = 3);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].opcode[0] ^= 1);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].range_first = 1);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].range_count = 1);
    REJECT_INVENTORY_MUTATION(trace_hooks[index].reserved[0] = 1);
  }

#undef REJECT_INVENTORY_MUTATION
}

int main(void)
{
  fixed_successor_boundary();
  kind3_successor_boundary();
  rejects_nonreviewed_topologies();
  rejects_marker_inventory_mutations();
  return 0;
}
