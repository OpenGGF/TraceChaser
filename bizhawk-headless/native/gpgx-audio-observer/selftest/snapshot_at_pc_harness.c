/* Executable proof for ACTION_SNAPSHOT_AT_PC, the parent-independent
   comparison-only observation used by the Sonic 3&K Play_Music mailbox
   boundary. It runs the real M68K core so the evidence is execution, not a
   fabricated event stream.

   It proves the action fires identically at root and under an active service,
   carries the active service token, emits the declared snapshot bytes, leaves
   the service stack untouched, and never faults on the active kind. */
#include <assert.h>
#include <stdint.h>
#include <string.h>
#include "m68kconf.h"
#include "m68k.h"
#include "audio_trace.h"

uint8_t zram[0x2000];
uint8_t work_ram[0x10000];
static uint8_t page0[0x10000];
static unsigned int invoke_irq_delay;
int vdp_68k_irq_ack(int int_level) { return int_level; }

static void write_fm(unsigned int address, unsigned int data)
{
  (void)address;
  gpgx_audio_trace_fm_write(1, data);
  if (invoke_irq_delay) { invoke_irq_delay = 0; m68k_set_irq_delay(0); }
}

static void put_byte(unsigned int address, uint8_t value)
{
  page0[address ^ 1u] = value;
}

static void base_config(struct gpgx_audio_trace_config_v1 *config)
{
  memset(config, 0, sizeof(*config));
  config->magic = 0x31544147; config->abi_version = 5; config->struct_size = 64;
  config->kind_size = 16; config->hook_size = 32; config->range_size = 16;
  config->event_size = 32; config->max_depth = 8; config->max_opcode_bytes = 8;
  config->reset_service_kind = 1; config->watch_mask_bytes = 8192;
  config->event_capacity = 65536; config->max_service_tokens_per_frame = 65535;
}

int main(void)
{
  struct gpgx_audio_trace_config_v1 config;
  struct gpgx_audio_service_kind_v1 kind;
  struct gpgx_audio_service_hook_v1 hooks[4];
  struct gpgx_audio_snapshot_range_v1 ranges[2];
  struct gpgx_audio_trace_event events[32];
  uint8_t mask[8192];
  const uint8_t setup[4] = { 0x10, 0x3c, 0x00, 0x7f };
  const uint8_t store[4] = { 0x11, 0xc0, 0x40, 0x00 };
  uint32_t count;
  unsigned int i;
  unsigned int markers = 0, snapshots = 0;

  memset(&kind, 0, sizeof(kind)); memset(hooks, 0, sizeof(hooks));
  memset(ranges, 0, sizeof(ranges)); memset(mask, 0, sizeof(mask));
  memset(page0, 0, sizeof(page0)); memset(zram, 0, sizeof(zram));
  base_config(&config);
  config.kind_count = 1; config.hook_count = 4; config.range_count = 2;
  config.snapshot_bytes_total = 3;

  kind.kind_id = 1; kind.flags = 4; kind.cancellation_range_count = 1;
  ranges[0].range_id = 1; ranges[0].start = 0; ranges[0].length = 1;
  ranges[1].range_id = 2; ranges[1].start = 0x1c0a; ranges[1].length = 1;
  zram[0x1c0a] = 0xfe;

  /* push kind 1 at the store, observe under it, pop, observe at root */
  hooks[0].hook_token = 1; hooks[0].action = 1; hooks[0].cpu = 2; hooks[0].pc = 4;
  hooks[0].service_kind = 1; hooks[0].opcode_length = 4;
  memcpy(hooks[0].opcode, store, 4);
  hooks[1].hook_token = 2; hooks[1].action = 13; hooks[1].cpu = 2; hooks[1].pc = 8;
  hooks[1].range_first = 1; hooks[1].range_count = 1; hooks[1].opcode_length = 2;
  hooks[1].opcode[0] = 0x4e; hooks[1].opcode[1] = 0x71;
  hooks[2].hook_token = 3; hooks[2].action = 2; hooks[2].cpu = 2; hooks[2].pc = 10;
  hooks[2].expected_active_kind = 1; hooks[2].range_count = 1;
  hooks[2].opcode_length = 2; hooks[2].opcode[0] = 0x4e; hooks[2].opcode[1] = 0x71;
  /* the root-side hook is the zero-range form: a parent-independent marker
     that carries no snapshot, which the accepted configuration proves is
     admitted and still observed */
  hooks[3].hook_token = 4; hooks[3].action = 13; hooks[3].cpu = 2; hooks[3].pc = 12;
  hooks[3].opcode_length = 2;
  hooks[3].opcode[0] = 0x4e; hooks[3].opcode[1] = 0x71;

  for (i = 0; i < 4; i++) put_byte(i, setup[i]);
  for (i = 0; i < 4; i++) put_byte(i + 4, store[i]);
  for (i = 8; i < 16; i += 2) { put_byte(i, 0x4e); put_byte(i + 1, 0x71); }
  m68k.memory_map[0].base = page0; m68k.memory_map[0].write8 = write_fm;

  /* --- configuration negatives ------------------------------------- */
  {
    struct gpgx_audio_service_hook_v1 bad[4];
    struct gpgx_audio_trace_config_v1 local;
    unsigned int attempt;
    for (attempt = 0; attempt < 6; attempt++)
    {
      base_config(&local);
      local.kind_count = 1; local.hook_count = 4; local.range_count = 2;
      local.snapshot_bytes_total = 3;
      memcpy(bad, hooks, sizeof(bad));
      switch (attempt)
      {
        case 0: local.abi_version = 4; break;          /* action needs ABI 5 */
        case 1: bad[1].service_kind = 1; break;        /* claims a service */
        case 2: bad[1].expected_active_kind = 1; break;/* claims a parent */
        case 3: bad[1].cpu = 1; break;                 /* Z80 is not allowed */
        case 4: bad[1].flags = 1; break;               /* no flags permitted */
        case 5: bad[3].pc = 8; break;                  /* not sole hook at PC */
        default: break;
      }
      assert(gpgx_audio_trace_configure(&local, mask, &kind, bad, ranges) != 0);
    }
  }
  assert(gpgx_audio_trace_configure(&config, mask, &kind, hooks, ranges) == 0);
  m68k_init(); m68k_set_reg(M68K_REG_PC, 0);
  m68k.pref_addr = ~0u; m68k.cycles = 0; m68k.refresh_cycles = 1000000;
  invoke_irq_delay = 1;
  assert(gpgx_audio_trace_begin_frame() == 0);
  m68k_run(400);
  assert(gpgx_audio_trace_end_frame() == 0);
  assert(gpgx_audio_trace_drain(events, 32, &count) == 0);

  for (i = 0; i < count; i++)
  {
    if (events[i].kind == 10 && events[i].value == 5)
    {
      markers++;
      if (events[i].subject == 2)
      {
        /* under the active service: it carries that service's token and the
           following service end proves the stack was never disturbed */
        assert(events[i].pc == 8 && events[i].source_cpu == 2);
        assert(events[i].service_token != 0 && events[i].service_kind == 1);
      }
      else
      {
        /* at root: no owner, and still observed */
        assert(events[i].subject == 4 && events[i].pc == 12);
        assert(events[i].service_token == 0 && events[i].service_kind == 0);
      }
    }
    if (events[i].kind == 6 && events[i].subject == 2)
    {
      snapshots++;
      assert(events[i].payload_length == 1 && events[i].payload[0] == 0xfe);
    }
  }
  /* fires once under a service and once at root; only the ranged form
     carries snapshot bytes */
  assert(markers == 2);
  assert(snapshots == 1);
  /* the pop still happened exactly once, so the action pushed nothing */
  {
    unsigned int ends = 0;
    for (i = 0; i < count; i++) if (events[i].kind == 2) ends++;
    assert(ends == 1);
  }
  {
    struct gpgx_audio_trace_first_fault_v1 fault;
    assert(gpgx_audio_trace_first_fault(&fault) == 0);
    assert(fault.reason == 0);
  }

  return 0;
}
