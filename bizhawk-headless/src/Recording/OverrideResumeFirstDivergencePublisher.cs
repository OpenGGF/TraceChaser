using System;
using System.Collections.Generic;
using System.IO;

namespace OpenGGF.BizHawk.Headless
{
    /// <summary>Closed four-file, first-publication-only fixture installer.</summary>
    internal sealed class OverrideResumeFirstDivergencePublisher
    {
        private static readonly string[] FileNames =
        {
            "s1/s1-override-resume-reference.v1.jsonl.gz",
            "s1/s1-override-resume-metadata.v1.json",
            "s2/s2-override-resume-reference.v1.jsonl.gz",
            "s2/s2-override-resume-metadata.v1.json"
        };
        private readonly OverrideResumeFirstDivergenceExtractor extractor;
        private readonly NoReplacePublisher publisher;

        internal OverrideResumeFirstDivergencePublisher(
            OverrideResumeFirstDivergenceExtractor value,
            NoReplacePublisher noReplace)
        {
            extractor=value??throw new ArgumentNullException("value");
            publisher=noReplace??throw new ArgumentNullException("noReplace");
        }

        internal void Publish(OverrideResumeFirstDivergenceExtractor.Inputs inputs,
            string tracechaserRoot,string inputRepositoryRoot,string fixtureRoot)
        {
            if(string.IsNullOrEmpty(inputRepositoryRoot)
                ||!Path.IsPathRooted(inputRepositoryRoot)
                ||!Directory.Exists(inputRepositoryRoot))
                throw new ArgumentException("Input repository root must be an existing absolute directory.");
            string expectedTracechaser=LinuxPathEntry.ResolveProposedPath(
                Path.Combine(inputRepositoryRoot,"tools","tracechaser"));
            if(string.IsNullOrEmpty(tracechaserRoot)
                ||!Path.IsPathRooted(tracechaserRoot)
                ||!Directory.Exists(tracechaserRoot)
                ||LinuxPathEntry.ResolveProposedPath(tracechaserRoot)
                    !=expectedTracechaser)
                throw new ArgumentException(
                    "TraceChaser root must be the pinned consumer submodule path.");
            string expected=LinuxPathEntry.ResolveProposedPath(Path.Combine(
                inputRepositoryRoot,"src","test","resources","audio","parity"));
            string requested=LinuxPathEntry.ResolveProposedPath(fixtureRoot);
            if(expected!=requested)
                throw new ArgumentException(
                    "Fixture root must be the requested consumer audio/parity subtree.");
            ValidateGameDirectories(requested);
            foreach(string name in FileNames)
                if(LinuxPathEntry.Exists(Path.Combine(requested,name)))
                    throw new IOException("Final output already exists and will not be replaced: "
                        +Path.Combine(requested,name));

            OverrideResumeFirstDivergenceExtractor.Output output=extractor.Extract(inputs);
            ValidateGameDirectories(requested);
            byte[][] values=
            {
                output.S1.ReferenceGzip,output.S1.MetadataUtf8,
                output.S2.ReferenceGzip,output.S2.MetadataUtf8
            };
            using(NoReplacePublisher.StagedPublicationSet staged=
                publisher.StageAllBytes(requested,FileNames,values))
            {
                staged.Publish();
            }
        }

        private static void ValidateGameDirectories(string fixtureRoot)
        {
            foreach(string game in new[]{"s1","s2"})
            {
                string path=Path.Combine(fixtureRoot,game);
                if(!LinuxPathEntry.Exists(path))continue;
                if(LinuxPathEntry.IsSymbolicLink(path))
                    throw new IOException("Fixture game directory must not be a symbolic link: "
                        +path);
                if(!Directory.Exists(path))
                    throw new IOException("Fixture game path must be a directory: "+path);
            }
        }
    }

    internal sealed class OverrideResumePublisherCommandOptions
    {
        internal const string Mode="--override-resume-first-divergence-publisher";
        private OverrideResumePublisherCommandOptions(string tracechaserRoot,
            string inputRoot,string fixtureRoot,
            OverrideResumeFirstDivergenceExtractor.Inputs inputs)
        {TracechaserRoot=tracechaserRoot;InputRoot=inputRoot;
            FixtureRoot=fixtureRoot;Inputs=inputs;}
        internal string TracechaserRoot {get;private set;}
        internal string InputRoot {get;private set;}
        internal string FixtureRoot {get;private set;}
        internal OverrideResumeFirstDivergenceExtractor.Inputs Inputs
        {get;private set;}

        internal static bool IsRequested(string[] args)
        {return args!=null&&args.Length!=0&&args[0]==Mode;}

        internal static OverrideResumePublisherCommandOptions Parse(string[] args)
        {
            if(!IsRequested(args))throw new ArgumentException(
                "Override-resume publisher mode is required.");
            var values=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int index=1;index<args.Length;index+=2)
            {
                string name=args[index];
                if(!Supported(name))throw new ArgumentException(
                    "Unknown override-resume publisher argument: "+name+".");
                if(index+1>=args.Length||string.IsNullOrEmpty(args[index+1]))
                    throw new ArgumentException(name+" requires a value.");
                if(values.ContainsKey(name))throw new ArgumentException(
                    "Duplicate override-resume publisher argument: "+name+".");
                values.Add(name,args[index+1]);
            }
            string trace=ExistingDirectory(Required(values,"--tracechaser-root"),
                "TraceChaser root");
            string input=ExistingDirectory(Required(values,
                "--input-repository-root"),"input repository root");
            string fixture=Absolute(Required(values,"--fixture-root"),
                "fixture root");
            return new OverrideResumePublisherCommandOptions(trace,input,fixture,
                new OverrideResumeFirstDivergenceExtractor.Inputs(
                    ExistingFile(Required(values,"--s1-raw-1"),"S1 raw 1"),
                    ExistingFile(Required(values,"--s1-attestation-1"),"S1 attestation 1"),
                    ExistingFile(Required(values,"--s1-raw-2"),"S1 raw 2"),
                    ExistingFile(Required(values,"--s1-attestation-2"),"S1 attestation 2"),
                    ExistingFile(Required(values,"--s2-raw-1"),"S2 raw 1"),
                    ExistingFile(Required(values,"--s2-attestation-1"),"S2 attestation 1"),
                    ExistingFile(Required(values,"--s2-raw-2"),"S2 raw 2"),
                    ExistingFile(Required(values,"--s2-attestation-2"),"S2 attestation 2")));
        }

        private static bool Supported(string value)
        {switch(value){case "--tracechaser-root":case "--input-repository-root":
            case "--fixture-root":case "--s1-raw-1":case "--s1-attestation-1":
            case "--s1-raw-2":case "--s1-attestation-2":case "--s2-raw-1":
            case "--s2-attestation-1":case "--s2-raw-2":case "--s2-attestation-2":
                return true;default:return false;}}
        private static string Required(IDictionary<string,string> values,string name)
        {string value;if(!values.TryGetValue(name,out value))throw new ArgumentException(
            "Required override-resume publisher argument is missing: "+name+".");return value;}
        private static string ExistingFile(string path,string label)
        {string full=Absolute(path,label);if(!File.Exists(full))throw new ArgumentException(
            label+" must be an existing absolute file.");return full;}
        private static string ExistingDirectory(string path,string label)
        {string full=Absolute(path,label);if(!Directory.Exists(full))throw new ArgumentException(
            label+" must be an existing absolute directory.");return full;}
        private static string Absolute(string path,string label)
        {if(string.IsNullOrEmpty(path)||!Path.IsPathRooted(path))throw new ArgumentException(
            label+" must be absolute.");return Path.GetFullPath(path);}
    }
}
