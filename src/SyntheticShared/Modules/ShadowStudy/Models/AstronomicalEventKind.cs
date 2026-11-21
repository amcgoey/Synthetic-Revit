namespace Synthetic.Modules.ShadowStudy.Models
{
    /// <summary>
    /// Identifies a solar astronomical event for hemisphere-aware calendar resolution and output naming.
    /// </summary>
    public enum AstronomicalEventKind
    {
        /// <summary>Custom user calendar date with no astronomical event identity.</summary>
        None = 0,

        /// <summary>Vernal (Spring) Equinox (Mar 20 North / Sep 22 South).</summary>
        SpringEquinox = 1,

        /// <summary>Summer Solstice (Jun 21 North / Dec 21 South).</summary>
        SummerSolstice = 2,

        /// <summary>Autumnal (Fall) Equinox (Sep 22 North / Mar 20 South).</summary>
        FallEquinox = 3,

        /// <summary>Winter Solstice (Dec 21 North / Jun 21 South).</summary>
        WinterSolstice = 4
    }
}
