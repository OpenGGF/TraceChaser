namespace OpenGGF.BizHawk.Headless
{
    /// <summary>
    /// The single closed raw authority for the Sonic 3&amp;K Sonic/Tails
    /// pre-consumption music-mailbox diagnostic. It is deliberately separate
    /// from the Knuckles complete-run authority: a different movie, interval,
    /// manifest, schema and purpose. It carries no fixture-publication right.
    /// </summary>
    internal static class S3kPreconsumptionRequestRawAuthority
    {
        internal static readonly S3kRawAudioAuthority Instance =
            Create();

        private static S3kRawAudioAuthority Create()
        {
            var authority = new S3kRawAudioAuthority(
                S3kPreconsumptionRequestProfile.Schema,
                S3kPreconsumptionRequestProfile.RomSha1,
                S3kPreconsumptionRequestProfile.MovieSha256,
                S3kPreconsumptionRequestProfile.ManifestSha256,
                S3kPreconsumptionRequestProfile.FirstRow,
                S3kPreconsumptionRequestProfile.ExclusiveEnd,
                S3kPreconsumptionRequestProfile.DriverStateStart,
                S3kPreconsumptionRequestProfile.DriverStateExclusiveEnd,
                false, true);
            authority.MailboxRangeId =
                S3kPreconsumptionRequestProfile.MailboxRangeId;
            authority.SubmissionEndPc =
                S3kPreconsumptionRequestProfile.EndPc;
            return authority;
        }
    }
}
