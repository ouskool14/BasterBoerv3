shader_type spatial;

uniform float height_scale : hint_range(0.0, 50.0) = 25.0;
uniform float moisture_contrast : hint_range(0.5, 2.0) = 1.2;
uniform float grass_coverage : hint_range(0.0, 1.0) = 0.5;
uniform sampler2D height_noise_tex : filter_linear;
uniform sampler2D moisture_noise_tex : filter_linear;

// Tiling scale for world-position UV
const float WORLD_UV_SCALE = 0.05;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Vertex color from TerrainGenerator.GetSouthAfricanTerrainColor
	vec3 base_color = COLOR.rgb;

	// Subtle world-position variation to break repetition
	vec2 wuv = world_pos.xz * WORLD_UV_SCALE;
	float noise_val = texture(height_noise_tex, wuv).r;
	float moisture_val = texture(moisture_noise_tex, wuv).r;

	// Apply moisture contrast to shift colours slightly
	moisture_val = pow(moisture_val, 1.0 / moisture_contrast);

	// Warm tint from height noise, cool tint from moisture
	vec3 warm_shift = vec3(0.04, -0.02, -0.03);  // push toward red soil
	vec3 cool_shift = vec3(-0.02, 0.03, 0.01);   // push toward green grass
	base_color += warm_shift * (noise_val - 0.5) + cool_shift * (moisture_val - 0.5);
	base_color = clamp(base_color, vec3(0.0), vec3(1.0));

	// Determine roughness: rock (gray) is smoother, grass/dirt is rougher
	// Rock tends toward equal R/G/B; grass has higher G relative to R
	float gray = dot(base_color, vec3(0.299, 0.587, 0.114));
	float color_variance = length(base_color - vec3(gray));
	// More variance = more grass/dirt = rougher; less = rock = smoother
	float roughness = mix(0.6, 0.9, clamp(color_variance * 5.0, 0.0, 1.0));

	ALBEDO = base_color;
	ROUGHNESS = roughness;
	SPECULAR = 0.2;
}
