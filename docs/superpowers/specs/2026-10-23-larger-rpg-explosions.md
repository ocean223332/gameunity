# Larger RPG explosions — 2026-10-23

User requested larger, more beautiful and spectacular RPG explosion VFX. Continue within the previously selected 60 FPS/lower-GPU goal. This is a visual iteration on the existing bounded ParticleSystem implementation.

Design: a brief hot central flash, a roughly 3–4 metre layered orange fireball, outward sparks, textured rolling smoke and an expanding ground dust ring. Fire cools from yellow to orange and dark red instead of remaining a bright round blob. Smoke uses a small shared procedural cloud texture with irregular edges. Ground dust is omitted for high airbursts. Keep eight impact slots and shared assets; no dynamic shadow lights, damage-radius changes or extra colliders. Clear and recycle every added layer with the existing effect lifetime.

Plan: observe new impact-layer tests fail; extend the existing pool/material setup and bursts; verify all effects tests plus real graphics captures at flash/fire/smoke stages; inspect the images and iterate; build Windows. No additional design question is necessary: the requested aesthetic direction and performance constraint are already clear.
