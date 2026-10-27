using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using spyglass.src.Client;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL " + label);
    checks++;
    Console.WriteLine("PASS " + label);
}
void Near(double actual, double expected, string label, double tolerance = 0.0001)
    => Check(Math.Abs(actual - expected) <= tolerance, $"{label}: {actual:0.######}");
float Rain(float intensity = 1, float strength = 1)
    => AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Rain, intensity, strength);
Near(1 - Math.Exp(-Rain() * 256), .15, "full rain loses 15% contrast at 256 blocks");
Near(1 - Math.Exp(-AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Snow, 1, 1) * 256), .25, "full snow loses 25% at 256 blocks");
Near(Rain(.5f), Rain() / 2, "actual precipitation intensity scales extinction");
Near(Rain(1, 2), Rain() * 2, "strength scales extinction");
Near(Rain(0), 0, "clear weather adds no attenuation");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, .6f, 112, 110), .6f, "exposed lens follows actual rainfall");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, 1, 112, 114), 0, "roof shelters the lens from rain impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, 0, 112, 110), 0, "dry weather produces no lens impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Snow, 1, 112, 110), 0, "snow does not produce liquid rain impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Hail, 1, 112, 110), 0, "hail does not produce liquid rain impacts");
Near(AtmosphericOpticsModel.LensRainIntensity(OpticsPrecipitation.Rain, 1, double.NaN, 110), 0, "invalid camera height cannot enable rain impacts");
Near(AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Hail, 1, 1), Rain(), "hail uses rain optics");
Near(AtmosphericOpticsModel.Extinction(OpticsPrecipitation.None, 1, 1), 0, "no precipitation adds no attenuation");
Check(AtmosphericOpticsModel.ResolvePrecipitation(OpticsPrecipitation.Auto, -1, 0) == OpticsPrecipitation.Snow
    && AtmosphericOpticsModel.ResolvePrecipitation(OpticsPrecipitation.Auto, 1, 0) == OpticsPrecipitation.Rain,
    "automatic rain/snow follows actual temperature threshold");
Check(AtmosphericOpticsModel.ResolvePrecipitation(OpticsPrecipitation.Rain, -10, 0) == OpticsPrecipitation.Rain,
    "explicit weather type wins over temperature");
Near(AtmosphericOpticsModel.Strength(float.NaN), 1, "invalid strength uses default");
Near(AtmosphericOpticsModel.Strength(-1), 0, "negative strength disabled");
Near(AtmosphericOpticsModel.Strength(5), 2, "strength capped at two");
Near(AtmosphericOpticsModel.Approach(0, 1, float.NaN), 0, "invalid delta time is safe");
Near(AtmosphericOpticsModel.Approach(AtmosphericOpticsModel.Approach(0, 1, .1f), 1, .1f),
    AtmosphericOpticsModel.Approach(0, 1, .2f), "weather smoothing independent of frame subdivision");
Near(AtmosphericOpticsModel.Glare(1, 1, 0, 0, 1), .1, "default maximum glare ten percent");
Near(AtmosphericOpticsModel.Glare(1, 1, 0, 0, 2), .2, "strong maximum glare twenty percent");
Near(AtmosphericOpticsModel.Glare(-.1f, 1, 0, 0, 1), 0, "no night sun glare");
Near(AtmosphericOpticsModel.Glare(1, 0, 0, 0, 1), 0, "engine sun occlusion suppresses glare");
Near(AtmosphericOpticsModel.Glare(1, 1, 1, 0, 1), 0, "dense clouds suppress glare");
Check(AtmosphericOpticsModel.Glare(1, 1, 0, .05f, 1) < .01f, "dense fog suppresses glare");
foreach (var (w, h) in new[] { (1920, 1080), (720, 1280), (800, 800) })
{
    var a = LensAperture.Square(0, 0, w, h, 10, .5f);
    Near(a.Width * a.Height / Math.Pow(Math.Min(w, h) - 20, 2), .25, $"quarter area at {w}x{h}");
    Check(a.Width == a.Height && a.X + a.Width / 2 == w / 2d && a.Y + a.Height / 2 == h / 2d,
        $"square centered at {w}x{h}");
}
Near(LensAperture.Square(0, 0, 100, 100, 100, .5f).Width, 0, "tiny window cannot produce negative aperture");
Near(LensAperture.Square(0, 0, 100, 100, -100, .5f).Width, 50, "negative margin cannot exceed viewport");

// Standard OpenGL projection, column-major, matching the game's matrix storage.
double[] Projection(double fov, double near, double far, double aspect = 1)
{
    double f = 1 / Math.Tan(fov / 2);
    return [f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, -(far + near) / (far - near), -1,
        0, 0, -2 * far * near / (far - near), 0];
}
float[] Inverse(double[] projection)
{
    var result = new float[16];
    Check(AtmosphericOpticsModel.InvertProjection(projection, result), "projection invertible");
    return result;
}
foreach (var (fov, near, far) in new[] { (1.2, .1, 2048d), (.12, .01, 1024d), (.8, .5, 4096d) })
{
    var p = Projection(fov, near, far, 16d / 9);
    var inv = Inverse(p);
    double[] point = [15, -8, -256, 1];
    double[] Mul(double[] m, double[] v) => Enumerable.Range(0, 4).Select(r => Enumerable.Range(0, 4).Sum(c => m[c * 4 + r] * v[c])).ToArray();
    var clip = Mul(p, point);
    var restored = Mul(inv.Select(x => (double)x).ToArray(), clip);
    for (int i = 0; i < 3; i++) Near(restored[i] / restored[3], point[i], $"view point reconstruction fov={fov} axis={i}", .1);
}
Check(!AtmosphericOpticsModel.InvertProjection(new double[16], new float[16]), "singular projection rejected");

Check(typeof(LensRainModel).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).All(t => t.Name is not "Crystal" and not "IceFacet"),
    "lens surface model has no crystal geometry types");
Check(typeof(LensRainModel).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
    .All(f => !f.Name.Contains("Frost", StringComparison.OrdinalIgnoreCase)), "lens surface model has no frost state or GPU arrays");
var stationary = new LensRainModel();
stationary.AddDrop(.2f, .25f, .008f);
for (int i = 0; i < 600; i++) stationary.Update(1f / 60, 0);
Near(stationary.Drops[0].Y, .25, "small beads adhere to their impact point");
Check(stationary.Drops[0].Fade < 1 && stationary.Drops[0].Fade > .7f, "adhering droplets gradually evaporate");
var flowing = new LensRainModel(); flowing.AddDrop(.5f, .2f, .025f);
var moving = flowing.Drops[0];
for (int i = 0; i < 180; i++) flowing.Update(1f / 60, 0);
Check(moving.Moving && moving.Y > .4f, "large droplets overcome adhesion and flow downward");
Check(flowing.Stamps.Any(s => s.Kind == LensRainModel.StampKind.Trail && s.Y1 > s.Y0), "flow deposits swept persistent trails");
Check(flowing.Stamps.Any(s => s.Kind == LensRainModel.StampKind.Erase), "flow absorbs small beads along its path");
Check(moving.Radius < .025f && moving.Stretch > 0, "flow sheds volume and stretches the upper tail");
var merging = new LensRainModel();
merging.AddDrop(.5f, .5f, .021f); merging.AddDrop(.51f, .5f, .021f);
merging.Update(1f / 60, 0);
Check(merging.Drops.Count == 1, "touching droplets merge");
Near(merging.Drops[0].Radius, Math.Sqrt(2) * .021, "merge conserves droplet area");
Near(merging.Drops[0].X, .505, "adhering merged droplet uses weighted position");
merging.AddDrop(.5f, .5f, .04f); merging.Update(1f / 60, 0);
Near(merging.Drops[0].Radius, LensRainModel.MaxDropRadius, "merged droplets have a radius cap");
// A full pool of adhering beads must still admit fresh impacts without
// discarding the visible stationary water or interrupting active flow.
var saturated = new LensRainModel();
for (int i = 0; i < LensRainModel.MaxDrops; i++)
    saturated.AddDrop(.05f + i % 10 * .09f, .05f + i / 10 * .11f, .008f);
var settledBead = saturated.Drops[0]; settledBead.Fade = .2f;
var activeBead = saturated.Drops[1]; activeBead.Moving = true; activeBead.Fade = .001f;
var pendingFlow = saturated.Drops[2]; pendingFlow.Radius = .02f; pendingFlow.Fade = .001f;
saturated.AcknowledgeSurface();
saturated.AddDrop(.5f, .1f, .025f);
Check(saturated.Drops.Count == LensRainModel.MaxDrops && saturated.Drops.Any(d => d.Radius == .025f),
    "saturated adhering beads leave room for a new flowing impact");
Check(!saturated.Drops.Contains(settledBead) && saturated.Stamps.Any(s => s.Kind == LensRainModel.StampKind.Small
    && s.X0 == settledBead.X && s.Y0 == settledBead.Y && s.Radius == settledBead.Radius && s.Height == .5f * settledBead.Fade),
    "displaced adhering bead retains its location size and fade in the persistent map");
Check(saturated.Drops.Contains(activeBead) && saturated.Drops.Contains(pendingFlow),
    "pool turnover preserves moving drops and drops large enough to start flowing");
var allFlowing = new LensRainModel();
for (int i = 0; i < LensRainModel.MaxDrops; i++) { allFlowing.AddDrop(.5f, .1f, .02f); allFlowing.Drops[^1].Moving = true; }
var activeDrops = allFlowing.Drops.ToArray(); allFlowing.AcknowledgeSurface();
allFlowing.AddDrop(.5f, .1f, .025f);
Check(allFlowing.Drops.SequenceEqual(activeDrops) && allFlowing.Stamps.Count == 0,
    "a pool full of active flow stays bounded without replacing moving drops");

// Acknowledge every frame as the renderer does: old queued trail commands
// must not mask a later stall after the physics pool fills up.
foreach (float intensity in new[] { 1f, .5f, .2f })
{
    var continuous = new LensRainModel();
    int gap = 0, longestGap = 0, windowFlow = 0, minimumWindowFlow = 600;
    bool bounded = true;
    for (int tick = 0; tick < 180 * 60; tick++)
    {
        continuous.Update(1f / 60, intensity);
        bounded &= continuous.Drops.Count <= LensRainModel.MaxDrops && continuous.Stamps.Count <= 4096;
        if (tick >= 600)
        {
            bool freshTrail = continuous.Stamps.Any(s => s.Kind == LensRainModel.StampKind.Trail && s.Y1 > s.Y0);
            gap = freshTrail ? 0 : gap + 1; longestGap = Math.Max(longestGap, gap);
            if (freshTrail) windowFlow++;
            if ((tick + 1) % 600 == 0) { minimumWindowFlow = Math.Min(minimumWindowFlow, windowFlow); windowFlow = 0; }
        }
        continuous.AcknowledgeSurface();
    }
    Check(bounded, $"three minutes of rain at {intensity} keeps water resources bounded");
    Check(longestGap < 600, $"three minutes of rain at {intensity} has no ten-second flow stall (max {longestGap / 60f:0.00}s)");
    Check(minimumWindowFlow >= 60, $"rain at {intensity} deposits fresh trails throughout every ten-second window");
    for (int tick = 0; tick < 60 * 60; tick++) { continuous.Update(1f / 60, 0); continuous.AcknowledgeSurface(); }
    Check(!continuous.HasWater, $"saturated rain at {intensity} still dries within a minute under shelter");
}
var wet = new LensRainModel();
for (int i = 0; i < 1800; i++) wet.Update(1f / 60, 1);
Check(wet.HasWater && wet.Drops.Count <= LensRainModel.MaxDrops && wet.Stamps.Count <= 4096, "sustained rain bounds drops and deferred GPU commands");
Check(wet.Drops.Count(d => d.Radius < .012f) >= 10, "sustained rain retains numerous adhering small beads");
Check(wet.Stamps.Any(s => s.Kind == LensRainModel.StampKind.Small) && wet.Stamps.Any(s => s.Kind == LensRainModel.StampKind.Trail),
    "sustained rain deposits both fine droplets and flowing trails");
for (int i = 0; i < 3600; i++) wet.Update(1f / 60, 0);
Check(!wet.HasWater, "droplets and residual trails dry within a minute of sheltered observation");
var rate30 = new LensRainModel(); var rate60 = new LensRainModel();
for (int i = 0; i < 600; i++) rate30.Update(1f / 30, 1);
for (int i = 0; i < 1200; i++) rate60.Update(1f / 60, 1);
Check(rate30.DropUniforms.SequenceEqual(rate60.DropUniforms) && rate30.DropShapeUniforms.SequenceEqual(rate60.DropShapeUniforms)
    && rate30.Stamps.SequenceEqual(rate60.Stamps), "flow and deposits agree at 30 and 60 FPS");
var wide = new LensRainModel(); var squareWater = new LensRainModel(); wide.SetAspect(2, 1);
wide.AddDrop(.5f, .1f, .03f); squareWater.AddDrop(.5f, .1f, .03f);
for (int i = 0; i < 60; i++) { wide.Update(1f / 60, 0); squareWater.Update(1f / 60, 0); }
Near(wide.Drops[0].Y, squareWater.Drops[0].Y, "flow speed uses physical lens proportions");
int priorGeneration = rate30.Generation;
rate30.Clear();
Check(!rate30.HasWater && rate30.Stamps.Count == 0 && rate30.Generation != priorGeneration
    && rate30.DropUniforms.All(v => v == 0) && rate30.DropShapeUniforms.All(v => v == 0), "reset invalidates persistent textures and pending deposits");
var idle = new LensRainModel();
idle.AddDrop(.5f, .5f, .015f);
idle.Update(1f / 60, 0);
idle.UpdateUsage(100000, true);
Check(idle.IsInUse && idle.Drops.Count > 0, "using a lens records active use with surface effects");
idle.UpdateUsage(129999, false);
Check(idle.HasWater, "surface effects survive an interruption shorter than 30 seconds");
idle.UpdateUsage(130000, false);
Check(!idle.HasWater && !idle.IsInUse, "30 seconds without use clears surface beads");
Check(idle.DropUniforms.All(v => v == 0) && idle.DropShapeUniforms.All(v => v == 0), "idle expiry clears GPU surface data without a render frame");
idle.UpdateUsage(140000, true); idle.AddDrop(.5f, .5f, .015f);
idle.UpdateUsage(169000, false); idle.UpdateUsage(169000, true); idle.UpdateUsage(180000, false);
Check(idle.HasWater, "resuming observation restarts the idle timeout");
idle.UpdateUsage(199000, false);
Check(!idle.HasWater, "the restarted timeout expires from the latest use");
idle.AddDrop(.5f, .5f, .015f); idle.UpdateUsage(100, false);
Check(idle.HasWater, "a new session clock does not cause an invalid immediate idle expiry");
idle.UpdateUsage(30100, false);
Check(!idle.HasWater, "idle expiry resumes normally after a session clock reset");

using var window = new NativeWindow(new NativeWindowSettings {
    ClientSize = new Vector2i(65, 65), StartVisible = false, StartFocused = false,
    API = ContextAPI.OpenGL, APIVersion = new Version(3, 3), Profile = ContextProfile.Core,
    Flags = ContextFlags.ForwardCompatible, Title = "Spyglass optics regression"
});
window.Context.MakeCurrent();
// Game uses the OpenGL bindings (rather than OpenGL4).
GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
Console.WriteLine("GPU " + GL.GetString(StringName.Renderer));
int Compile(ShaderType type, string path)
{
    int id = GL.CreateShader(type);
    string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shaders", path));
    GL.ShaderSource(id, source);
    GL.CompileShader(id);
    GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
    Check(status != 0, "actual " + type + " compiles: " + GL.GetShaderInfoLog(id));
    return id;
}
int vertex = Compile(ShaderType.VertexShader, "spyglassforkatmosphere.vsh");
int fragment = Compile(ShaderType.FragmentShader, "spyglassforkatmosphere.fsh");
int program = GL.CreateProgram();
GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
Check(linked != 0, "actual optics shader links: " + GL.GetProgramInfoLog(program));
GL.DeleteShader(vertex); GL.DeleteShader(fragment);
GL.UseProgram(program);
Check(GL.GetUniformLocation(program, "frostCount") == -1 && GL.GetUniformLocation(program, "frostFacets") == -1,
    "production shader exposes no frost rendering uniforms");
int U(string name) => GL.GetUniformLocation(program, name);
void F(string name, float value) => GL.Uniform1(U(name), value);
int Texture(int size, PixelInternalFormat format, PixelFormat pixelFormat, float[] pixels)
{
    int id = GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D, id);
    GL.TexImage2D(TextureTarget.Texture2D, 0, format, size, size, 0, pixelFormat, PixelType.Float, pixels);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    return id;
}
GL.ActiveTexture(TextureUnit.Texture0);
const int Size = 65;
int scene = Texture(1, PixelInternalFormat.Rgba32f, PixelFormat.Rgba, [.2f, .2f, .2f, .7f]);
int depthTexture = Texture(1, PixelInternalFormat.R32f, PixelFormat.Red, [.5f]);
int output = Texture(Size, PixelInternalFormat.Rgba32f, PixelFormat.Rgba, new float[Size * Size * 4]);
int framebuffer = GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, output, 0);
Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete, "test framebuffer complete");
int vao = GL.GenVertexArray(), vbo = GL.GenBuffer();
GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
float[] vertices = [-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, 1, 1, 0, -1, 1, 0];
GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
GL.Viewport(0, 0, Size, Size); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend);
GL.Uniform1(U("sceneTexture"), 0); GL.Uniform1(U("depthTexture"), 1);
GL.Uniform2(U("frameSize"), (float)Size, (float)Size);
GL.Uniform3(U("fogColor"), .8f, .8f, .8f);
GL.Uniform4(U("colorGrade"), 1f, 1f, 1f, 0f); F("sepia", 0);
GL.Uniform2(U("sunScreen"), .5f, .5f); GL.Uniform3(U("sunViewDirection"), 0f, 0f, -1f);
var projection = Projection(1.2, .1, 4096);
GL.UniformMatrix4(U("inverseProjection"), 1, false, Inverse(projection));
void Depth(float distance)
{
    float d = distance == float.PositiveInfinity ? 1 : (float)((projection[10] * -distance + projection[14]) / distance * .5 + .5);
    GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, depthTexture);
    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1, PixelFormat.Red, PixelType.Float, new[] { d });
}
float[] Render(float distance = 256, float coefficient = -1, float day = 1, float glare = 0, float fade = 1, bool square = false)
{
    Depth(distance);
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
    F("extinction", coefficient < 0 ? Rain() : coefficient); F("dayWeight", day); F("glareStrength", glare); F("fade", fade);
    GL.Uniform4(U("aperture"), square ? .25f : 0, square ? .25f : 0, square ? .5f : 1, square ? .5f : 1);
    GL.ClearColor(.031f, .031f, .031f, 1); GL.Clear(ClearBufferMask.ColorBufferBit);
    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
    var pixels = new float[Size * Size * 4];
    GL.ReadPixels(0, 0, Size, Size, PixelFormat.Rgba, PixelType.Float, pixels);
    return pixels;
}
float Pixel(float[] pixels, int x = 32, int y = 32) => pixels[(y * Size + x) * 4];
Near(Pixel(Render()), .29, "GPU rain attenuation at 256 blocks", .0005);
Near(Pixel(Render(coefficient: AtmosphericOpticsModel.Extinction(OpticsPrecipitation.Snow, 1, 1))), .35, "GPU snow attenuation at 256 blocks", .0005);
Check(Pixel(Render(32)) < Pixel(Render(256)), "GPU distant surfaces lose more contrast");
Check(Pixel(Render(), 60, 32) > Pixel(Render()), "GPU off-axis pixels use radial distance, not just view depth");
Near(Pixel(Render(3000, Rain(1, 2))), .38, "GPU additional weather blend capped at thirty percent", .0005);
Near(Pixel(Render(float.PositiveInfinity)), .2, "GPU sky retains engine fog");
Near(Pixel(Render(day: 0)), .2, "GPU full night never brightens dark target");
GL.Uniform4(U("colorGrade"), 1.8f, 1.2f, 1.4f, .1f); F("sepia", .3f);
Near(Pixel(Render(day: 0)), .2, "GPU night clamp remains effective with non-default color grading");
GL.Uniform4(U("colorGrade"), 1f, 1f, 1f, 0f); F("sepia", 0);
GL.Uniform3(U("fogColor"), .05f, .05f, .05f);
Check(Pixel(Render(day: 0)) < .2, "GPU night scattering may darken bright targets");
GL.Uniform3(U("fogColor"), .8f, .8f, .8f);
Near(Pixel(Render(coefficient: 0)), .2, "GPU disabled weather preserves scene");
Near(Pixel(Render(fade: 0)), .2, "GPU zero zoom fade preserves scene");
Near(Pixel(Render(fade: .5f)), .245, "GPU optics fades with zoom animation", .0005);
var cropped = Render(square: true);
Near(Pixel(cropped, 2, 2), .031, "GPU does not draw outside square opening");
Near(Pixel(cropped), .29, "GPU square opening receives weather", .0005);
var halo = Render(coefficient: 0, glare: .1f);
Near(Pixel(halo), .28, "GPU local sun halo strength", .0005);
Near(Pixel(Render(coefficient: 0, glare: .5f)), .36, "GPU sun halo capped at twenty percent", .0005);
GL.Uniform2(U("frameSize"), Size * 2f, Size * 2f);
Near(Pixel(Render(coefficient: 0, glare: .1f), 35, 32), Pixel(halo, 35, 32), "GPU halo size follows physical aperture across render scales");
GL.Uniform2(U("frameSize"), (float)Size, (float)Size);
Check(Pixel(halo, 10, 10) < .201, "GPU halo does not whiten entire view");
GL.Uniform2(U("sunScreen"), -.5f, -.5f);
Near(Pixel(Render(coefficient: 0, glare: .1f, square: true)), .2, "GPU offscreen sun halo disappears");
GL.Uniform2(U("sunScreen"), .5f, .5f);
// A zero glare weight from night/occlusion must leave all pixels unchanged.
Near(Pixel(Render(coefficient: 0, glare: AtmosphericOpticsModel.Glare(1, 0, 0, 0, 1))), .2, "GPU engine-occluded sun contributes no glare");
Near(halo[(32 * Size + 32) * 4 + 3], .7, "GPU scene alpha preserved");

using var surface = new LensRainSurface(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shaders", "spyglassforkrain.vsh")),
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shaders", "spyglassforkrain.fsh")));
void UploadWater(LensRainModel model)
{
    using (var state = new OpticsRenderState()) surface.Update(model, 512, 512);
    var error = GL.GetError(); if (error != ErrorCode.NoError) throw new Exception("Surface upload GL: " + error);
    GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, surface.BigTexture); GL.BindSampler(2, 0);
    GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D, surface.SmallTexture); GL.BindSampler(3, 0);
    GL.Uniform1(U("rainBigTexture"), 2); GL.Uniform1(U("rainSmallTexture"), 3);
    GL.Uniform2(U("rainTexel"), 1f / surface.Width, 1f / surface.Height);
}
float[] ReadWater(int texture)
{
    using var state = new OpticsRenderState();
    GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, texture);
    var values = new float[surface.Width * surface.Height * 2];
    GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rg, PixelType.Float, values);
    return values;
}
void SolidScene(float value)
{
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float,
        new[] {value, value, value, .7f});
}
var waterModel = new LensRainModel(); waterModel.AddDrop(.5f, .5f, .024f); waterModel.Update(1f / 60, 0);
F("lensRainStrength", 1); F("lensLight", 1); UploadWater(waterModel);
var beads = Render(coefficient: 0);
Check(beads.All(v => float.IsFinite(v)) && !beads.SequenceEqual(Render(coefficient: 0, fade: 0)), "GPU rounded height field affects transmitted scene");
Near(Pixel(beads, 2, 2), .2, "GPU dry glass outside droplet is unchanged");
Near(beads[(32 * Size + 32) * 4 + 3], .7, "GPU water preserves scene alpha");
F("lensRainStrength", 0); var disabledRain = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => Math.Abs(disabledRain[i * 4] - .2f) < .0001f), "GPU disabled water preserves all pixels");
F("lensRainStrength", 1);
Check(disabledRain.SequenceEqual(Render(coefficient: 0, fade: 0)), "GPU water fades with zoom");
Near(Pixel(Render(coefficient: 0, square: true), 2, 2), .031, "GPU water stays inside aperture");
waterModel.Clear(); UploadWater(waterModel);
Check(disabledRain.SequenceEqual(Render(coefficient: 0)), "GPU clear invalidates all persistent water");

// A real flowing drop must leave a texture behind after its head exits.
var runoff = new LensRainModel(); runoff.AddDrop(.5f, .1f, .03f);
for (int i = 0; i < 240; i++) { runoff.Update(1f / 60, 0); UploadWater(runoff); }
Check(runoff.Drops.Count == 0, "flowing head exits through lower lens boundary");
var residual = ReadWater(surface.SmallTexture);
float TrailPeak(float[] data) => Enumerable.Range(0, data.Length / 2).Max(i => data[i * 2 + 1]);
float SmallPeak(float[] data) => Enumerable.Range(0, data.Length / 2).Max(i => data[i * 2]);
Check(TrailPeak(residual) > .01f && SmallPeak(residual) > 0, "GPU persistent trails and shed beads survive departed head");
var trailImage = Render(coefficient: 0);
Check(!trailImage.SequenceEqual(disabledRain), "GPU residual trails refract and shade without a large bead");
for (int i = 0; i < 60; i++) { runoff.Update(1f / 60, 0); UploadWater(runoff); }
Check(TrailPeak(ReadWater(surface.SmallTexture)) < TrailPeak(residual) * .65f, "GPU trails evaporate after head departs");
runoff.Clear(); UploadWater(runoff);
Check(ReadWater(surface.SmallTexture).All(v => v == 0) && ReadWater(surface.BigTexture).All(v => v == 0), "GPU clear zeros both water targets");

// Controlled deposits test channel separation and historical aging on late uploads.
var channels = new LensRainModel();
channels.Stamps.Add(new(.5f,.5f,.5f,.5f,.02f,.5f,.1f,LensRainModel.StampKind.Small,0));
channels.Stamps.Add(new(.5f,.4f,.5f,.6f,.01f,.5f,.1f,LensRainModel.StampKind.Trail,0));
UploadWater(channels); var beforeErase = ReadWater(surface.SmallTexture);
channels.Stamps.Add(new(.5f,.4f,.5f,.6f,.03f,0,0,LensRainModel.StampKind.Erase,0));
UploadWater(channels); var afterErase = ReadWater(surface.SmallTexture);
Check(SmallPeak(afterErase) == 0 && TrailPeak(afterErase) == TrailPeak(beforeErase), "GPU passing bead absorbs fine drops while preserving deposited trail channel");
using (var state = new OpticsRenderState()) surface.Update(channels, 256, 256);
var resizedTrail = TrailPeak(ReadWater(surface.SmallTexture));
Check(resizedTrail > TrailPeak(afterErase) * .4f && resizedTrail < TrailPeak(afterErase) * .6f,
    "GPU resize preserves deposited trail with scaled physical height");
using (var state = new OpticsRenderState()) surface.Update(channels, 2048, 1024);
Check(surface.Width == 1024 && surface.Height == 512 && TrailPeak(ReadWater(surface.SmallTexture)) > 0,
    "GPU large displays cap texture size while preserving aspect and trails");
channels.Clear(); UploadWater(channels);
channels.Stamps.Add(new(.5f,.5f,.5f,.5f,.02f,.5f,.1f,LensRainModel.StampKind.Small,0));
for (int i = 0; i < 600; i++) channels.Update(1f / 60, 0);
UploadWater(channels);
Check(SmallPeak(ReadWater(surface.SmallTexture)) < SmallPeak(beforeErase) * .3f, "GPU deferred deposits retain their true age");

var pattern = new float[Size * Size * 4];
for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
{
    int j = (y * Size + x) * 4;
    pattern[j] = pattern[j + 1] = pattern[j + 2] = ((x / 3 + y / 3) % 2 == 0) ? .1f : .7f;
    pattern[j + 3] = .7f;
}
GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, scene);
GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, Size, Size, 0, PixelFormat.Rgba, PixelType.Float, pattern);
GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
F("lensLight", 0); F("lensRainStrength", 0); var plainPattern = Render(coefficient: 0);
var patternBead = new LensRainModel(); patternBead.AddDrop(.5f, .5f, .04f); patternBead.Update(1f / 60, 0);
UploadWater(patternBead); F("lensRainStrength", 1); var beadPattern = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).Any(i => Math.Abs(beadPattern[i * 4] - plainPattern[i * 4]) > .08f), "GPU water displaces patterned background even with zero lens light");
foreach (float background in new[] { .82f, .95f, 1f })
{
    SolidScene(background); F("lensLight", 1);
    var skyBead = Render(float.PositiveInfinity, coefficient: 0);
    Check(Enumerable.Range(0, Size * Size).Any(i => skyBead[i * 4] < background - .04f), $"GPU water has visible rim on flat bright sky {background}");
    Near(Pixel(skyBead, 2, 2), background, "GPU surrounding sky remains unchanged");
}
SolidScene(0); F("lensLight", 0);
var unlit = Render(coefficient: 0);
Check(Enumerable.Range(0, Size * Size).All(i => unlit[i * 4] == 0), "GPU black scene stays black with water");
patternBead.Clear(); UploadWater(patternBead);
SolidScene(.2f); F("lensLight", 1); F("lensRainStrength", 0);
GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, 0);
GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D, 0);

GL.Enable(EnableCap.DepthTest); GL.Enable(EnableCap.Blend); GL.Enable(EnableCap.CullFace); GL.DepthMask(true);
GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.Zero);
int sampler = GL.GenSampler(); GL.BindSampler(0, sampler);
GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(TextureTarget.Texture2D, depthTexture); GL.BindSampler(3, sampler);
GL.Enable(EnableCap.ScissorTest); GL.Scissor(3, 4, 7, 8); GL.Viewport(1, 2, 31, 32);
GL.ClearColor(.1f, .2f, .3f, .4f); GL.ColorMask(true, false, true, false);
GL.BlendEquationSeparate(BlendEquationMode.FuncReverseSubtract, BlendEquationMode.FuncSubtract);
GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
try
{
    using var state = new OpticsRenderState();
    GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace); GL.DepthMask(false);
    surface.Update(new LensRainModel(), 256, 128);
    GL.BlendFunc(BlendingFactor.One, BlendingFactor.One);
    GL.UseProgram(0); GL.BindVertexArray(0); GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, 0); GL.BindSampler(0, 0);
    GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, 0);
    GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, scene); GL.BindSampler(2, sampler);
    throw new InvalidOperationException("simulated draw failure");
}
catch (InvalidOperationException) { }
Check(GL.IsEnabled(EnableCap.DepthTest) && GL.IsEnabled(EnableCap.Blend) && GL.IsEnabled(EnableCap.CullFace)
    && GL.GetBoolean(GetPName.DepthWritemask), "GL state restored after a draw exception");
Check(GL.GetInteger(GetPName.CurrentProgram) == program && GL.GetInteger(GetPName.VertexArrayBinding) == vao
    && GL.GetInteger(GetPName.ArrayBufferBinding) == vbo, "GL shader and geometry bindings restored");
Check(GL.GetInteger(GetPName.BlendSrcRgb) == (int)BlendingFactorSrc.SrcAlpha
    && GL.GetInteger(GetPName.BlendDstRgb) == (int)BlendingFactorDest.OneMinusSrcAlpha
    && GL.GetInteger(GetPName.BlendSrcAlpha) == (int)BlendingFactorSrc.One
    && GL.GetInteger(GetPName.BlendDstAlpha) == (int)BlendingFactorDest.Zero, "GL separate blend factors restored after lens drawing");
Check(GL.GetInteger(GetPName.ActiveTexture) == (int)TextureUnit.Texture3, "GL active texture unit restored");
GL.ActiveTexture(TextureUnit.Texture0);
Check(GL.GetInteger(GetPName.TextureBinding2D) == scene && GL.GetInteger(GetPName.SamplerBinding) == sampler,
    "GL scene texture and sampler restored");
GL.ActiveTexture(TextureUnit.Texture1);
Check(GL.GetInteger(GetPName.TextureBinding2D) == depthTexture, "GL depth texture restored");
GL.ActiveTexture(TextureUnit.Texture2);
Check(GL.GetInteger(GetPName.TextureBinding2D) == 0 && GL.GetInteger(GetPName.SamplerBinding) == 0,
    "GL unrelated third texture unit remains intact after lens rendering");
GL.ActiveTexture(TextureUnit.Texture3);
Check(GL.GetInteger(GetPName.TextureBinding2D) == depthTexture && GL.GetInteger(GetPName.SamplerBinding) == sampler,
    "GL fourth texture and sampler restored after surface pass");
var restoredViewport = new int[4]; GL.GetInteger(GetPName.Viewport, restoredViewport);
var restoredClear = new float[4]; GL.GetFloat(GetPName.ColorClearValue, restoredClear);
var restoredMask = new bool[4]; GL.GetBoolean(GetPName.ColorWritemask, restoredMask);
Check(restoredViewport.SequenceEqual(new[] {1,2,31,32}) && GL.IsEnabled(EnableCap.ScissorTest), "GL viewport and scissor restored before game composite");
Check(GL.GetInteger(GetPName.DrawFramebufferBinding) == framebuffer && GL.GetInteger(GetPName.ReadFramebufferBinding) == 0,
    "GL draw and read framebuffer restored independently");
Check(restoredMask.SequenceEqual(new[] {true,false,true,false}) && Math.Abs(restoredClear[2] - .3f) < .0001f,
    "GL color mask and clear color restored");
Check(GL.GetInteger(GetPName.BlendEquationRgb) == (int)BlendEquationMode.FuncReverseSubtract
    && GL.GetInteger(GetPName.BlendEquationAlpha) == (int)BlendEquationMode.FuncSubtract, "GL separate blend equations restored");
GL.Disable(EnableCap.ScissorTest); GL.ColorMask(true,true,true,true); GL.BlendEquation(BlendEquationMode.FuncAdd);
var finalError = GL.GetError(); Check(finalError == ErrorCode.NoError, "no OpenGL errors: " + finalError);
if (args.Length == 2 && args[0] == "--preview")
    OpticsPreview.Render(program, vao, Path.GetFullPath(args[1]));
GL.DeleteSampler(sampler); GL.DeleteBuffer(vbo); GL.DeleteVertexArray(vao); GL.DeleteFramebuffer(framebuffer);
GL.DeleteTexture(scene); GL.DeleteTexture(depthTexture); GL.DeleteTexture(output); GL.DeleteProgram(program);
Console.WriteLine($"SUCCESS {checks} assertions");
