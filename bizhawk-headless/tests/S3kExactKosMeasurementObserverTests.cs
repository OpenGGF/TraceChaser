using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
namespace OpenGGF.BizHawk.Headless.Tests
{
 internal static class S3kExactKosMeasurementObserverTests
 {
  public static void Register(ICollection<TestMain.TestCase> tests)
  {
   tests.Add(new TestMain.TestCase("ExactKosMeasurement same-frame child costs one opportunity",SameFrame));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement queued child excludes waiting",Waiting));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement exact direct is opt-in and avoids module duplicate",Direct));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement consumes literal cursor before shift",Cursor));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement unknown callers are excluded",Caller));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement empty service adds no cost",Empty));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement reset aborts pending work",Reset));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement revision check precedes registration",Revision));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement caller opcode check precedes registration",CallerRevision));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement interrupted head preserves descriptor",Interrupted));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement missing or malformed capture identity registers nothing",Identity));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement service-time descriptor change reports frame, hook, caller and FIFO",ServiceFailureContext));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement retirement-time tail change reports frame, hook and FIFO",RetirementFailureContext));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement failed load-time v2 capture publishes neither file",FailedCapturePublishesNothing));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement head bookmarked after both EndReached stores resumes",BookmarkedAfterBothStores));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement head bookmarked after the EndReached source store resumes",BookmarkedAfterSourceStore));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement enqueue while the head is bookmarked in EndReached",SubmittedWhileBookmarked));
   tests.Add(new TestMain.TestCase("ExactKosMeasurement bookmarked head accepts only its own EndReached cursors",BookmarkedNegativeControls));
  }
  static byte[] Rom()
  {
   var rom=new byte[0x6520];foreach(int source in new[]{0x100,0x200})Array.Copy(new byte[]{0x17,0,(byte)'A',(byte)'B',(byte)'C',0,0,0},0,rom,source,8);
   Put(rom,0x1B46,new byte[]{0,0x38,0,0x80,0xFF,0x60,0x4E,0x75});
   Put(rom,0x1BEE,new byte[]{0x4E,0x75,0x4A,0x78,0xFF,0x0E});
   Put(rom,0x1C1A,new byte[]{0x4A,0x78,0xFF,0x0E,0x67,0,0,0xDA,0x6B,0,0,0xD8});
   Put(rom,0x1CE4,new byte[]{0x67,0x14,0x41,0xF8,0xFF,0x40,0x43,0xF8});
   Put(rom,0x6516,new byte[]{0x4E,0xB9,0,0,0x1C,0x1A});
   Put(rom,0x62D2,new byte[]{0x4E,0xB9,0,0,0x1C,0x1A});return rom;
  }
  static void Put(byte[] rom,int at,byte[] value){Array.Copy(value,0,rom,at,value.Length);}
  static FakeS1Host Host(){var h=new FakeS1Host(null);h.Ram[0xF600]=0xC;h.SetCpuRegister("M68K A7",0xFFFFF000);h.SetU32(0xF000,0x651C);return h;}
  static S3kExactKosMeasurementObserver Observer(StringWriter w,byte[] rom=null){return new S3kExactKosMeasurementObserver(rom??Rom(),w,"fixture.bk2",new string('a',64),"CFBF98C36C776677290A872547AC47C53D2761D6");}
  static void Stage(FakeS1Host h,int slot,uint source){h.SetU32(0xFF40+slot*8,source);h.SetU32(0xFF44+slot*8,0xFFFF8000);h.SetU16(0xFF0E,(ushort)(slot+1));}
  static void Retire(FakeS1Host h,int remaining,uint source){h.SetU32(0xFF40,source+8);h.SetU32(0xFF44,0xFFFF8003);h.SetU16(0xFF0E,(ushort)remaining);h.FireExecuteCallback(0x1CE4);}
  static JObject Row(StringWriter w,int index=0){return JObject.Parse(w.ToString().TrimEnd().Split('\n')[index]);}
  static void SameFrame(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);}AssertEx.Equal(1,(int)Row(w)["service_opportunities"]);AssertEx.Equal("exact_fifo_callbacks",(string)Row(w)["observation_precision"]);}
  static void Waiting(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);Stage(h,1,0x200);h.FireExecuteCallback(0x1B46);for(int i=0;i<3;i++)h.FireExecuteCallback(0x1C1A);Retire(h,1,0x100);Stage(h,0,0x200);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x200);}AssertEx.Equal(3,(int)Row(w)["service_opportunities"]);AssertEx.Equal("head_service_censored",(string)Row(w)["observation_precision"]);AssertEx.Equal(1,(int)Row(w,1)["service_opportunities"]);AssertEx.Equal(1,(int)Row(w,1)["sequence_in_frame"]);}
  static void Direct(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0,true)){Stage(h,0,0x100);h.SetU32(0xF000,0x1B46);h.FireExecuteCallback(0x1BEE);h.FireExecuteCallback(0x1B46);h.SetU32(0xF000,0x651C);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);Stage(h,0,0x200);h.SetU32(0xF000,0x5AE00);h.FireExecuteCallback(0x1BEE);h.SetU32(0xF000,0x651C);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x200);}AssertEx.Equal(0x1B46,(int)Row(w)["submission_pc"]);AssertEx.Equal(0x1BEE,(int)Row(w,1)["submission_pc"]);}
  static void Cursor(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);h.SetU16(0xFF0E,0);AssertEx.Throws<InvalidDataException>(()=>h.FireExecuteCallback(0x1CE4),"consumed descriptor/count");AssertEx.Equal("",w.ToString());h.SetU32(0xFF40,0x108);h.SetU32(0xFF44,0xFFFF8003);h.FireExecuteCallback(0x1CE4);}AssertEx.Equal(1,(int)Row(w)["service_opportunities"]);}
  static void Caller(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.SetU32(0xF000,0x4064);h.FireExecuteCallback(0x1C1A);h.SetU32(0xF000,0x651C);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);}AssertEx.Equal(false,(bool)Row(w)["classified"]);}
  static void Empty(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0)){h.FireExecuteCallback(0x1C1A);Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);}AssertEx.Equal(1,(int)Row(w)["service_opportunities"]);}
  static void Reset(){var h=Host();var w=new StringWriter();var o=Observer(w);using(o.Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);o.Reset();h.SetU16(0xFF0E,0);h.FireExecuteCallback(0x1C1A);Stage(h,0,0x200);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x200);}AssertEx.Equal(1,(int)Row(w)["epoch"]);AssertEx.Equal(0,(int)Row(w)["ordinal"]);}
  static void Revision(){var rom=Rom();rom[0x1CE4]=0;var h=Host();var w=new StringWriter();AssertEx.Throws<InvalidDataException>(()=>Observer(w,rom).Attach(h,()=>0),"hook mismatch");AssertEx.Equal(null,h.ExecuteCallbackAddress);}
  static void CallerRevision(){var rom=Rom();rom[0x6516]=0;var h=Host();var w=new StringWriter();AssertEx.Throws<InvalidDataException>(()=>Observer(w,rom).Attach(h,()=>0),"hook mismatch");AssertEx.Equal(null,h.ExecuteCallbackAddress);}
  static void Interrupted(){var h=Host();var w=new StringWriter();using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);h.SetU16(0xFF0E,0x8001);h.SetU32(0xFF40,0x102);AssertEx.Throws<InvalidDataException>(()=>h.FireExecuteCallback(0x1C1A),"descriptor changed");h.SetU32(0xFF40,0x100);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);}AssertEx.Equal(2,(int)Row(w)["service_opportunities"]);}

  // A FIFO entry rewritten outside the observed enqueue/retirement path must still fail closed, and the failure
  // must name the exact raw frame, hook, caller, live count and the expected (ledger) and actual (RAM) entries.
  static void ServiceFailureContext()
  {
   var h=Host();var w=new StringWriter();int frame=4321;
   using(Observer(w).Attach(h,()=>frame))
   {
    Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);
    h.SetU32(0xFF40,0x200);                       // descriptor rewritten without a retirement
    var e=Catch(()=>h.FireExecuteCallback(0x1C1A));
    foreach(string part in new[]{"descriptor changed","raw_frame 4321","hook $1C1A","caller $00651C","count 1",
      "expected[0] 00000100>FFFF8000","actual[0] 00000200>FFFF8000"})
     if(e.Message.IndexOf(part,StringComparison.Ordinal)<0)throw new InvalidOperationException("missing <"+part+"> in: "+e.Message);
   }
   AssertEx.Equal("",w.ToString());
  }

  static void RetirementFailureContext()
  {
   var h=Host();var w=new StringWriter();int frame=77;
   using(Observer(w).Attach(h,()=>frame))
   {
    Stage(h,0,0x100);Stage(h,1,0x200);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);
    h.SetU32(0xFF48,0x300);                       // queued tail entry rewritten before the head retires
    h.SetU32(0xFF40,0x108);h.SetU32(0xFF44,0xFFFF8003);h.SetU16(0xFF0E,1);
    var e=Catch(()=>h.FireExecuteCallback(0x1CE4));
    foreach(string part in new[]{"descriptor changed","raw_frame 77","hook $1CE4","count 1","expected[1] 00000200>FFFF8000",
      "actual[1] 00000300>FFFF8000"})
     if(e.Message.IndexOf(part,StringComparison.Ordinal)<0)throw new InvalidOperationException("missing <"+part+"> in: "+e.Message);
   }
   AssertEx.Equal("",w.ToString());
  }

  static InvalidDataException Catch(Action action)
  {
   try{action();}catch(InvalidDataException e){return e;}
   throw new InvalidOperationException("Expected the exact observer to fail closed.");
  }

  // The load-time capture publishes v1 and v2 as one staged set: an exception inside the capture publishes neither.
  static void FailedCapturePublishesNothing()
  {
   string root=TestScratch.CreateRootPath("exact-kos-failed");
   Directory.CreateDirectory(root);
   try
   {
    string output=Path.Combine(root,"out");
    int code=global::BizHawk.Headless.Gpgx.Program.RunTraceCapture<object>(output,TextWriter.Null,TextWriter.Null,
     ()=>new NoDisposal(),()=>Host(),
     (host,writers)=>{writers[0].Write("{\"v1\":1}\n");writers[1].Write("{\"v2\":1}\n");throw new InvalidDataException("Exact Kos FIFO descriptor changed before committed retirement.");},
     ignored=>"",new NoReplacePublisher(),new[]{"load_time_measurements.jsonl","load_time_measurements_v2.jsonl"});
    if(code==0)throw new InvalidOperationException("A failed capture reported success.");
    AssertEx.Equal(false,File.Exists(Path.Combine(output,"load_time_measurements.jsonl")));
    AssertEx.Equal(false,File.Exists(Path.Combine(output,"load_time_measurements_v2.jsonl")));
   }
   finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }

  sealed class NoDisposal:IDisposable{public void Dispose(){}}

  // Process_Kos_Queue_EndReached stores the source cursor (1CD2), then the destination cursor (1CD6), before the
  // andi at 1CDA clears the busy bit. Set_Kos_Bookmark saves any rte address in [1C26,1CFA) while that bit is set,
  // so a V-int at 1CD6 or 1CDA leaves the head holding its own end cursors until Restore_Kos_Bookmark resumes from
  // the next 1C1A. The test ROM's job at 0x100 ends at 0x108>FFFF8003. Knux raw frame 27718 hit the 1CDA case.
  static void Bookmark(FakeS1Host h,uint destination,int count){h.SetU32(0xFF40,0x108);h.SetU32(0xFF44,destination);h.SetU16(0xFF0E,(ushort)(0x8000|count));}
  static void BookmarkedAfterBothStores()
  {
   var h=Host();var w=new StringWriter();
   using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);
    Bookmark(h,0xFFFF8003,1);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);}
   AssertEx.Equal(2,(int)Row(w)["service_opportunities"]);AssertEx.Equal(0x100,(int)Row(w)["source"]);
  }
  static void BookmarkedAfterSourceStore()
  {
   var h=Host();var w=new StringWriter();
   using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);
    Bookmark(h,0xFFFF8000,1);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x100);}
   AssertEx.Equal(2,(int)Row(w)["service_opportunities"]);
  }
  // Backup_Kos_Registers returns to the main loop, so Queue_Kos can append behind a bookmarked head that same frame.
  static void SubmittedWhileBookmarked()
  {
   var h=Host();var w=new StringWriter();
   using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);
    Bookmark(h,0xFFFF8003,1);h.SetU32(0xFF48,0x200);h.SetU32(0xFF4C,0xFFFF8000);h.SetU16(0xFF0E,0x8002);h.FireExecuteCallback(0x1B46);
    h.FireExecuteCallback(0x1C1A);Retire(h,1,0x100);Stage(h,0,0x200);h.FireExecuteCallback(0x1C1A);Retire(h,0,0x200);}
   AssertEx.Equal(2,(int)Row(w)["service_opportunities"]);AssertEx.Equal(0x200,(int)Row(w,1)["source"]);AssertEx.Equal(0x1B46,(int)Row(w,1)["submission_pc"]);
  }
  static void BookmarkedNegativeControls()
  {
   // wrong destination cursor; busy bit clear; destination stored before source; a non-head entry at its own end.
   foreach(Action<FakeS1Host> corrupt in new Action<FakeS1Host>[]{
     h=>Bookmark(h,0xFFFF8004,2),
     h=>{Bookmark(h,0xFFFF8003,2);h.SetU16(0xFF0E,2);},
     h=>{Bookmark(h,0xFFFF8003,2);h.SetU32(0xFF40,0x100);},
     h=>{h.SetU32(0xFF48,0x208);h.SetU32(0xFF4C,0xFFFF8003);h.SetU16(0xFF0E,0x8002);}})
   {
    var h=Host();var w=new StringWriter();
    using(Observer(w).Attach(h,()=>0)){Stage(h,0,0x100);Stage(h,1,0x200);h.FireExecuteCallback(0x1B46);h.FireExecuteCallback(0x1C1A);
     corrupt(h);AssertEx.Throws<InvalidDataException>(()=>h.FireExecuteCallback(0x1C1A),"descriptor changed");}
    AssertEx.Equal("",w.ToString());
   }
  }

  static void Identity()
  {
   var h=Host();var w=new StringWriter();
   foreach(string hash in new string[]{null,"","not-hex",new string('a',63)})
    AssertEx.Throws<ArgumentException>(()=>new S3kExactKosMeasurementObserver(Rom(),w,"fixture.bk2",hash,"CFBF98C36C776677290A872547AC47C53D2761D6"),"movie/locked-ROM identity");
   AssertEx.Throws<ArgumentException>(()=>new S3kExactKosMeasurementObserver(Rom(),w,"",new string('a',64),"CFBF98C36C776677290A872547AC47C53D2761D6"),"movie/locked-ROM identity");
   foreach(string romHash in new string[]{null,"",new string('b',40)})
    AssertEx.Throws<ArgumentException>(()=>new S3kExactKosMeasurementObserver(Rom(),w,"fixture.bk2",new string('a',64),romHash),"movie/locked-ROM identity");
   AssertEx.Equal(null,h.ExecuteCallbackAddress);AssertEx.Equal("",w.ToString());
  }

 }
}
