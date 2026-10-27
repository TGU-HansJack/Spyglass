using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace spyglass.src
{
    public enum VignetteStyle
    {
        square,
        none
    };

    class SpyglassConfig
    {
        public String vignetteStyle = "square";

        public float lensSize = 0.5f; // side length relative to the original square aperture; 0.5 gives one-quarter area
        public int edgeSize = 10; // square aperture margin along the shorter screen dimension, in pixels
        public float edgeOpacity = 0.5f; // legacy setting retained for config/network compatibility; metal is always opaque

        public float glassBrightness = 1.0f; // less then 1.0, will dim the view when using spyglass
        public float glassColor = 0.94f; // the brightness of the glass material, by default this is nearly white like quartz.

        public bool enableLensRain = true; // flowing droplets and persistent trails; clear after 30 seconds without use
        public float lensRainStrength = 1.0f; // 0..2; independent of atmospheric optics

        // Client preferences, intentionally excluded from the server vignette packet.
        public bool enableAtmosphericOptics = true;
        public float weatherOpticsStrength = 1.0f; // 0..2; 0 disables extra precipitation attenuation
        public float sunGlareStrength = 1.0f; // 0..2; 0 disables the local solar halo

        public float transitionTimeBasis = 525f; // ms to do a complete zoom ( rougly ), higher slows animation, lower quickens it.
        public float minimumZoomTime = 250f; // minimum time using spyglass.
        public float defaultZoomPosition = 0.85f;

        public float zoomWheelSpeedMultiplier = 1.0f;
        public bool invertZoomWheel = false;

        public float mouseSensitivityAdjustment = 2.0f; // 200% - raise to make panning slower when using spyglass

        public bool enableMouseWheelAdjustment = true; // mouse wheel while using spyglass adjustment
        public bool enableZoomInThirdPerson = true; // if disabled you will just see your character use the spyglass.

        public bool hideHUDWhileSpying = true; // removes minimap/coordinates/statsbar/hotbar to focus only on the what your looking at.
        public bool preserveZoomBetweenUses = false; // restores previous zoom when using spyglass

        public bool overrideClientConfig = false; // whether the server overrides connected clients' vignette settings
        
        public VignetteStyle GetVinetteStyle()
        {
            try
            {
                return Enum.Parse<VignetteStyle>(SpyglassMod.config.vignetteStyle);
            }
            catch { }

            return VignetteStyle.square;
        }
    }
}
