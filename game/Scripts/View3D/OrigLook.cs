using System.Collections.Generic;
using Godot;

namespace OpenGG.View3D;

/// <summary>
/// The look of the original's cars. Its engine lights them the old Direct3D way: per vertex, in the colour
/// numbers as they are stored (not in linear light), ambient plus one light's diffuse; the body meshes get
/// a second, additive pass (the cars' white <c>SpecMat</c> overlay), which we take for a specular highlight.
/// The light's direction, colours and the highlight's power were fitted to screenshots of the original.
/// </summary>
public static class OrigLook
{
    /// <summary>Ambient light (0..1 of each colour channel).</summary>
    public static Vector3 Ambient = new(0.127f, 0.127f, 0.127f);
    /// <summary>The light's diffuse strength.</summary>
    public static Vector3 Diffuse = new(0.476f, 0.476f, 0.476f);
    /// <summary>Towards the light, in the camera's space (x right, y up, z towards the viewer): in front of the
    /// car, up and a little to the left; it turns with the view.</summary>
    public static Vector3 Light = new(-0.250f, 0.644f, 0.723f);
    /// <summary>The highlight's strength and power (Blinn-Phong), and where its light is: behind the car, up
    /// and to the left (the highlight lights up surfaces seen edge-on that face up and left).</summary>
    public static Vector3 Specular = new(0.38f, 0.38f, 0.38f);
    public static float Power = 14f;
    public static Vector3 SpecLight = new(-0.207f, 0.766f, -0.609f);
    /// <summary>
    /// The Catalog's pictures: more ambient and a weaker light from the camera's left, fitted pixel by pixel on the
    /// original's Catalog (a Mustang's and a pickup's ENGINE and R GEAR pages, 68 000 pixels: 3.6 off on average in the
    /// colour numbers, 38.7 with the WorkShop's light). The pictures' materials take it by <see cref="ForCatalog"/>.
    /// </summary>
    public static readonly Vector3 CatalogAmbient = Vector3.One * 0.252f, CatalogDiffuse = Vector3.One * 0.288f,
        CatalogLight = new(-0.666f, -0.008f, 0.746f);
    /// <summary>Lab: 1 draws the camera-space normals instead (as colours, 0.5 + n / 2), 2 the paint white and
    /// everything else black, 3 each material's own colour at half strength (textured ones blue), 4 the
    /// material's number (red: n mod 85 times 3, green: n div 85 times 3; blue: body meshes).</summary>
    public static float Debug;

    static bool registered;

    static readonly Vector3 WorkshopAmbient = Ambient, WorkshopDiffuse = Diffuse, WorkshopLight = Light, WorkshopSpecular = Specular;

    /// <summary>The cars in the WorkShop (and wherever no other light is known): the WorkShop's fitted light.</summary>
    public static void UseWorkshop()
    {
        Ambient = WorkshopAmbient;
        Diffuse = WorkshopDiffuse;
        Light = WorkshopLight;
        Specular = WorkshopSpecular;
        Apply();
    }

    /// <summary>
    /// The cars on the Auction's stage: the WorkShop's light (turning with the view) with less ambient. Fitted face by
    /// face on a car of known parts on the original's stage (3.3 off on average in the colour numbers, 5.4 with the
    /// WorkShop's own values); a second car agreed on the weak ambient.
    /// </summary>
    public static void UseAuction()
    {
        Ambient = Vector3.One * 0.046f;
        Diffuse = Vector3.One * 0.532f;
        Light = WorkshopLight;
        Specular = WorkshopSpecular;
        Apply();
    }

    /// <summary>
    /// The cars in a scene lit as its own meshes are (<see cref="Model3ds.SceneLight"/>), the light given in the
    /// camera's space. Measured in the Car Lot: its cars take the same light as its asphalt and walls, which in this
    /// shader's terms (it doubles the colours) is half the scene's ambient and diffuse.
    /// </summary>
    public static void UseScene(Model3ds.SceneLight light, Vector3 towardsLightInView, bool highlight = true)
    {
        Ambient = Vector3.One * (light.Ambient / 2);
        Diffuse = Vector3.One * (light.Diffuse / 2);
        Light = towardsLightInView;
        Specular = highlight ? WorkshopSpecular : Vector3.Zero;
        Apply();
    }

    /// <summary>Sends the light to the shaders (after changing the fields).</summary>
    public static void Apply()
    {
        var rs = RenderingServer.Singleton;
        var values = new (string Name, Variant Value, RenderingServer.GlobalShaderParameterType Type)[]
        {
            ("orig_ambient", Ambient, RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_diffuse", Diffuse, RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_light", Light.Normalized(), RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_specular", Specular, RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_power", Power, RenderingServer.GlobalShaderParameterType.Float),
            ("orig_spec_light", SpecLight.Normalized(), RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_debug", Debug, RenderingServer.GlobalShaderParameterType.Float),
            ("orig_cat_ambient", CatalogAmbient, RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_cat_diffuse", CatalogDiffuse, RenderingServer.GlobalShaderParameterType.Vec3),
            ("orig_cat_light", CatalogLight.Normalized(), RenderingServer.GlobalShaderParameterType.Vec3),
        };
        foreach (var (name, value, type) in values)
        {
            if (!registered) RenderingServer.GlobalShaderParameterAdd(name, type, value);
            else RenderingServer.GlobalShaderParameterSet(name, value);
        }
        registered = true;
    }

    /// <summary>
    /// The shaders' colour numbers (as the original's, sRGB) to the light the renderer blends in and back. Forward+
    /// blends in linear light; the Compatibility renderer (OpenGL, Godot's fallback without Vulkan or Direct3D 12)
    /// blends the colour numbers themselves and shows ALBEDO as it is, so there both are the identity.
    /// </summary>
    public const string ColourSpace = """
        #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
        vec3 to_linear(vec3 c) { return c; }
        vec3 to_srgb(vec3 c) { return c; }
        #else
        vec3 to_linear(vec3 c) { return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045))); }
        vec3 to_srgb(vec3 c) { return mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, c * 12.92, lessThan(c, vec3(0.0031308))); }
        #endif
        """;

    /// <summary>How a material is blended: as the .car's blend class (604).</summary>
    public enum Blend { Opaque, Alpha, Add }

    static readonly Dictionary<(Blend, bool), Shader> shaders = [];

    static Shader ShaderFor(Blend blend, bool flat)
    {
        if (!registered) Apply();
        if (shaders.TryGetValue((blend, flat), out var s)) return s;
        string mode = blend switch
        {
            Blend.Alpha => "blend_premul_alpha, depth_draw_never",
            Blend.Add => "blend_add, depth_draw_never",
            _ => "depth_draw_opaque",
        };
        string interp = flat ? "flat " : "";
        // A flat face is lit by its own normal (the importer puts it in the vertex colour), a smooth one by its corners'.
        string normal = flat ? "(COLOR.rgb * 2.0 - 1.0)" : "NORMAL";
        string output = blend switch
        {
            // Glass: its own colour at its opacity, the highlight added on top at full strength.
            Blend.Alpha => "vec3 o = min(base * c.a + spec, vec3(1.0));\n\tALBEDO = to_linear(o);\n\tALPHA = c.a;",
            Blend.Add => "ALBEDO = to_linear(min(base + spec, vec3(1.0)));",
            _ => "ALBEDO = to_linear(min(base + spec, vec3(1.0)));",
        };
        s = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode unshaded, cull_disabled, {{mode}};

                uniform vec4 color = vec4(1.0);
                uniform sampler2D tex : repeat_enable, filter_linear_mipmap_anisotropic;
                uniform bool textured = false;
                uniform float shine = 0.0;
                uniform float is_paint = 0.0;
                uniform float mat_id = 0.0;
                uniform float catalog = 0.0;

                global uniform vec3 orig_ambient;
                global uniform vec3 orig_diffuse;
                global uniform vec3 orig_light;
                global uniform vec3 orig_specular;
                global uniform float orig_power;
                global uniform vec3 orig_spec_light;
                global uniform float orig_debug;
                global uniform vec3 orig_cat_ambient;
                global uniform vec3 orig_cat_diffuse;
                global uniform vec3 orig_cat_light;

                varying {{interp}}vec3 lit;
                varying {{interp}}vec3 spec;
                varying vec3 view_normal;

                {{ColourSpace}}

                void vertex() {
                	vec3 n = normalize(MODELVIEW_NORMAL_MATRIX * {{normal}});
                	view_normal = n;
                	vec3 p = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
                	bool cat = catalog > 0.5;
                	vec3 l = normalize(cat ? orig_cat_light : orig_light);
                	lit = (cat ? orig_cat_ambient : orig_ambient) + (cat ? orig_cat_diffuse : orig_diffuse) * max(dot(n, l), 0.0);
                	vec3 h = normalize(normalize(orig_spec_light) + normalize(-p));
                	spec = (cat ? 0.0 : shine) * orig_specular * pow(max(dot(n, h), 0.0), orig_power);
                }

                void fragment() {
                	vec4 c = color;
                	// The lit colour is clamped per vertex, as Direct3D does, then modulated twice over by the
                	// texture, or by white where there is none (fitted: chrome, glass and paint all come out at
                	// twice their colour times the light).
                	vec3 base = min(c.rgb * lit, vec3(1.0)) * 2.0;
                	if (textured) {
                		vec4 t = texture(tex, UV);
                		base *= t.rgb * (catalog > 0.5 && shine > 0.5 ? 0.5 : 1.0);
                		c.a *= t.a;
                	}
                	base = min(base, vec3(1.0));
                	{{output}}
                	if (orig_debug > 4.5) {
                		ALBEDO = to_linear(vec3(0.25, 0.5, 0.75));
                	} else if (orig_debug > 3.5) {
                		ALBEDO = to_linear(vec3(mod(mat_id, 85.0) * 3.0, floor(mat_id / 85.0) * 3.0, shine * 255.0) / 255.0);
                	} else if (orig_debug > 2.5) {
                		ALBEDO = to_linear(textured ? vec3(0.0, 0.0, 1.0) : color.rgb * 0.5 + vec3(0.5, 0.0, 0.0) * 0.0);
                	} else if (orig_debug > 1.5) {
                		ALBEDO = vec3(is_paint);
                	} else if (orig_debug > 0.5) {
                		ALBEDO = to_linear(clamp(normalize(view_normal) * 0.5 + 0.5, 0.0, 1.0));
                	}
                }
                """,
        };
        shaders[(blend, flat)] = s;
        return s;
    }

    /// <summary>A material of an original car: its colour (as stored, 0..1), texture, blending, flat or smooth
    /// shading, and whether it belongs to a body mesh (those get the highlight).</summary>
    public static ShaderMaterial Material(string name, Color color, Texture2D? texture, Blend blend, bool flat, bool shiny)
    {
        var m = new ShaderMaterial { ResourceName = name, Shader = ShaderFor(blend, flat) };
        m.SetShaderParameter("color", Raw(color));
        m.SetShaderParameter("shine", shiny ? 1f : 0f);
        if (texture is not null)
        {
            m.SetShaderParameter("tex", texture);
            m.SetShaderParameter("textured", true);
        }
        return m;
    }

    /// <summary>A colour for the shader as stored (a Color would be turned into linear light on the way).</summary>
    static Vector4 Raw(Color c) => new(c.R, c.G, c.B, c.A);

    /// <summary>The same material in another colour (paint).</summary>
    public static ShaderMaterial Recolored(ShaderMaterial template, Color color)
    {
        var m = (ShaderMaterial)template.Duplicate();
        m.SetShaderParameter("color", Raw(color));
        m.SetShaderParameter("is_paint", 1f);
        return m;
    }

    /// <summary>The same material drawing a picture (the paint picture) instead of its texture. Paint is
    /// modulated twice over by a white material (fitted to the original's screenshots: its paint is much
    /// brighter than the paint colour times the light).</summary>
    public static ShaderMaterial WithTexture(ShaderMaterial template, Texture2D texture)
    {
        var m = (ShaderMaterial)template.Duplicate();
        m.SetShaderParameter("color", Raw(Colors.White));
        m.SetShaderParameter("is_paint", 1f);
        m.SetShaderParameter("tex", texture);
        m.SetShaderParameter("textured", true);
        return m;
    }

    public static bool IsOrig(Material? m) => m is ShaderMaterial sm && shaders.ContainsValue(sm.Shader);

    static readonly Dictionary<ShaderMaterial, ShaderMaterial> catalogMats = [];

    /// <summary>The same material lit as the Catalog's pictures are (see <see cref="CatalogAmbient"/>).</summary>
    public static ShaderMaterial ForCatalog(ShaderMaterial m)
    {
        if (catalogMats.TryGetValue(m, out var c)) return c;
        c = (ShaderMaterial)m.Duplicate();
        c.SetShaderParameter("catalog", 1f);
        return catalogMats[m] = c;
    }

    // ---- a part in its condition's colour -------------------------------------------------------------------

    /// <summary>
    /// The colours a part takes in its condition (black, red, yellow, green), in colour numbers, before the light.
    /// Red and yellow fitted on the JunkYard's hovered single sheets (hood, fenders); green on the WorkShop's Show
    /// Condition (three views, 29000 pixels), which fits the JunkYard's green sheets as well. The weak channels come out
    /// about 15 in every colour (yellow's blue taken so: the captures keep blue in 5 bits). Black never shows in the
    /// yard but in the visit it came with, and keeps its own look under Show Condition; ours fits the WorkShop's black
    /// parts under the pointer (a transmission added about (41, 36, 33) onto the grey behind it). The last is black's in
    /// the JunkYard, fitted on the one seen there: a Left head added (64, 74, 82) onto the shelf's wood, OpenGG's
    /// WorkShop black the same part and view (35, 34, 31).
    /// </summary>
    public static readonly Vector3[] ConditionColours =
        [new Vector3(26, 26, 26) / 255f, new Vector3(204, 15, 13) / 255f, new Vector3(188, 181, 16) / 255f, new Vector3(17, 136, 17) / 255f,
         new Vector3(50, 61, 75) / 255f];

    /// <summary>The JunkYard's part under the pointer (black in its own colour, see <see cref="ConditionColours"/>).</summary>
    public static ShaderMaterial YardConditionFor(Material? original, int condition) =>
        Condition(condition == Core.Content.Condition.Black ? YardBlack : condition, IsFlat(original), shiny: false);

    const int YardBlack = 4;

    /// <summary>Lab: other condition colours (colour numbers 0..1), for fitting them.</summary>
    public static void SetConditionColours(Vector3[] colours)
    {
        for (int i = 0; i < colours.Length && i < ConditionColours.Length; i++) ConditionColours[i] = colours[i];
        foreach (var ((condition, _, _), m) in conditionMats)
        {
            var c = ConditionColours[condition];
            m.SetShaderParameter("color", new Vector4(c.X, c.Y, c.Z, 1));
        }
    }

    static readonly Dictionary<bool, Shader> conditionShaders = [];
    static readonly Dictionary<(int, bool, bool), ShaderMaterial> conditionMats = [];

    /// <summary>
    /// A part in its condition's colour, as the original draws Show Condition and the JunkYard's part under the pointer:
    /// lit like the cars (per vertex, the colour twice over), but added onto whatever
    /// is behind it, in the colour numbers, from both sides of every face and over everything in front of it (the
    /// scene shows through). Measured on the JunkYard's hovered parts.
    /// </summary>
    public static ShaderMaterial Condition(int condition, bool flat, bool shiny)
    {
        condition = Mathf.Clamp(condition, 0, ConditionColours.Length - 1);
        if (conditionMats.TryGetValue((condition, flat, shiny), out var m)) return m;
        var c = ConditionColours[condition];
        m = new ShaderMaterial { Shader = ConditionShader(flat) };
        m.SetShaderParameter("color", new Vector4(c.X, c.Y, c.Z, 1));
        m.SetShaderParameter("shine", shiny ? 1f : 0f);
        conditionMats[(condition, flat, shiny)] = m;
        return m;
    }

    /// <summary>The condition's material standing in for one of an original car's materials: lit flat or smooth as
    /// that one is.</summary>
    public static ShaderMaterial ConditionFor(Material? original, int condition)
    {
        // The body's highlight is not drawn over the condition's colour (a green roof on the captures is flat green).
        return Condition(condition, IsFlat(original), shiny: false);
    }

    static bool IsFlat(Material? m) =>
        m is ShaderMaterial sm && (sm.Shader == ShaderFor(Blend.Opaque, true) || sm.Shader == ShaderFor(Blend.Alpha, true) || sm.Shader == ShaderFor(Blend.Add, true));

    /// <summary>
    /// The WorkShop's part under the pointer, measured: drawn as Show Condition draws the parts, in its condition's
    /// colour added onto what is behind it instead of its own look (a green flywheel over a red block came out red and
    /// green together, (239, 174, 57), its own grey gone; a yellow door over the dark cab looks solid yellow), black
    /// parts too, in a dark grey (a black transmission added about (41, 36, 33) onto the grey behind it).
    /// </summary>
    public static ShaderMaterial HoverFor(Material? original, int condition) => ConditionFor(original, condition);

    static Shader ConditionShader(bool flat)
    {
        if (!registered) Apply();
        if (conditionShaders.TryGetValue(flat, out var s)) return s;
        string interp = flat ? "flat " : "";
        string normal = flat ? "(COLOR.rgb * 2.0 - 1.0)" : "NORMAL";
        s = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_add, depth_draw_never, depth_test_disabled, shadows_disabled;

                uniform vec4 color = vec4(1.0);
                uniform float shine = 0.0;
                uniform sampler2D under_tex : hint_screen_texture, filter_nearest;

                global uniform vec3 orig_ambient;
                global uniform vec3 orig_diffuse;
                global uniform vec3 orig_light;
                global uniform vec3 orig_specular;
                global uniform float orig_power;
                global uniform vec3 orig_spec_light;

                varying {{interp}}vec3 lit;
                varying {{interp}}vec3 spec;

                {{ColourSpace}}

                void vertex() {
                	vec3 n = normalize(MODELVIEW_NORMAL_MATRIX * {{normal}});
                	vec3 p = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
                	lit = orig_ambient + orig_diffuse * max(dot(n, normalize(orig_light)), 0.0);
                	vec3 h = normalize(normalize(orig_spec_light) + normalize(-p));
                	spec = shine * orig_specular * pow(max(dot(n, h), 0.0), orig_power);
                }

                void fragment() {
                	vec3 add = min(min(min(color.rgb * lit, vec3(1.0)) * 2.0, vec3(1.0)) + spec, vec3(1.0));
                	// Added in the colour numbers, as the original adds, on top of what is behind.
                	vec3 under = texture(under_tex, SCREEN_UV).rgb;
                	ALBEDO = to_linear(min(to_srgb(under) + add, vec3(1.0))) - under;
                }
                """,
        };
        conditionShaders[flat] = s;
        return s;
    }
}
