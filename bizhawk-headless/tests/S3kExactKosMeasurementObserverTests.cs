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
