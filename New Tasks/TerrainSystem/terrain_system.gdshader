shader_type spatial;

// Terrain System Shader
// Supports: height-based coloring, moisture, seasonal variation, road blending

// Core parameters
uniform float height_scale : hint_range(0.0, 100.0) = 35.0;
uniform float min_height : hint_range(-50.0, 50.0) = 0.0;
uniform float chunk_size : hint_range(64.0, 512.0) = 256.0;
uniform vec3 world_origin = vec3(0.0);

// Seasonal parameters (updated by TerrainSystem)
uniform float global_wetness : hint_range(0.0, 1.0) = 0.5;
uniform float global_dryness : hint_range(0.0, 1.0) = 0.5;
uniform float green_bias : hint_range(0.0, 1.0) = 0.5;

// Color palette - South African bushveld
uniform vec4 color_red_soil : source_color = vec4(0.65, 0.35, 0.25, 1.0);
uniform vec4 color_dry_grass : source_color = vec4(0.76, 0.70, 0.50, 1.0);
uniform vec4 color_green_grass : source_color = vec4(0.55, 0.65, 0.40, 1.0);
uniform vec4 color_rock : source_color = vec4(0.50, 0.45, 0.40, 1.0);
uniform vec4 color_road : source_color = vec4(0.75, 0.68, 0.55, 1.0);
uniform vec4 color_wet_soil : source_color = vec4(0.45, 0.35, 0.25, 1.0);

// Effect intensities
uniform float moisture_darkening : hint_range(0.0, 1.0) = 0.3;
uniform float dry_bleaching : hint_range(0.0, 1.0) = 0.25;
uniform float road_blend : hint_range(0.0, 1.0) = 0.8;
uniform float rock_blend : hint_range(0.0, 1.0) = 0.7;

// Vertex color channels encode:
// R = height factor
// G = moisture
// B = grass coverage
// A = rock factor

varying float v_height;
varying float v_moisture;
varying float v_rockiness;
varying float v_road_influence;
varying float v_slope;

void vertex() {
    // Extract data from vertex color (set by TerrainSystem)
    v_height = COLOR.r;
    v_moisture = COLOR.g;
    v_rockiness = COLOR.b;
    v_road_influence = COLOR.a;
    
    // Calculate slope from normal
    v_slope = 1.0 - NORMAL.y;
    
    // Flat shading - use face normal
    NORMAL = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
}

void fragment() {
    // Height normalization
    float height_norm = clamp((v_height - min_height) / height_scale, 0.0, 1.0);
    
    // Base color selection based on height and moisture
    vec4 base_color;
    
    if (height_norm < 0.25) {
        // Lower areas - soil dominant, wetter = darker
        float wet_factor = v_moisture * global_wetness;
        base_color = mix(color_red_soil, color_wet_soil, wet_factor * 0.5);
    } else if (height_norm < 0.6) {
        // Mid elevation - grassland with seasonal variation
        float green_factor = v_moisture * global_wetness * (0.5 + green_bias * 0.5);
        base_color = mix(color_dry_grass, color_green_grass, green_factor);
    } else {
        // Higher elevation - rockier, some grass in wet areas
        float grass_factor = v_moisture * global_wetness * 0.4;
        base_color = mix(color_rock, color_dry_grass, grass_factor);
    }
    
    // Blend in rock on steep slopes or rocky areas
    float rock_factor = max(v_rockiness, smoothstep(0.3, 0.6, v_slope));
    base_color = mix(base_color, color_rock, rock_factor * rock_blend);
    
    // Blend in roads
    base_color = mix(base_color, color_road, v_road_influence * road_blend);
    
    // Apply moisture darkening (wet ground is darker)
    float wet_darken = 1.0 - (v_moisture * global_wetness * moisture_darkening);
    base_color.rgb *= wet_darken;
    
    // Apply dry season bleaching
    float dry_bleach = 1.0 - (global_dryness * dry_bleaching * (1.0 - v_moisture));
    base_color.rgb = mix(base_color.rgb, vec3(0.85, 0.82, 0.75), global_dryness * dry_bleaching);
    
    // Add subtle variation based on world position
    vec3 world_pos = world_origin + VERTEX;
    float variation = sin(world_pos.x * 0.1) * sin(world_pos.z * 0.1) * 0.02;
    base_color.rgb += variation;
    
    // Output
    ALBEDO = base_color.rgb;
    
    // Slight roughness variation
    ROUGHNESS = 0.8 + v_moisture * 0.1;
    
    // Specular for wet areas
    SPECULAR = v_moisture * global_wetness * 0.2;
    
    // Normal for rock areas
    if (rock_factor > 0.5) {
        NORMAL_MAP_DEPTH = 0.3;
    }
}
