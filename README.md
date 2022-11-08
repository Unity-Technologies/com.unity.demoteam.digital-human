# Package: com.unity.demoteam.digital-human

Library of tech features used to realize the digital human from *The Heretic* and *Enemies*.

## Usage

Declare the package as a git dependency in `Packages/manifest.json`:

```
"dependencies": {
    "com.unity.demoteam.digital-human": "https://github.com/Unity-Technologies/com.unity.demoteam.digital-human.git",
    ...
}
```

## Requirements
-*Minimum Requirements*
	- Unity 2020.3 +
	- HDRP 10.9.0 +

- *Requirements for Skin Deformation and Skin Attachment GPU Path*
	- Unity 2021.2 +
	
- *Requirements for new Eye and Skin Shaders*
	- Unity 2022.2.0a16 +
	- HDRP 14.0.3 +

## Features

- *Facial animation systems*
  - Tools for 4D clip import and processing. <sub>(When we say 4D, we mean a sequence of meshes captured over time.)</sub>
  - 4D clip rendering with timeline integration.
  - 4D frame fitting allowing detail injection from facial rig.
  - Integration of facial rig from Snappers.
  - Pose facial rig directly in Unity.
  
- *Skin attachment system*
  - Drive meshes and transforms in relation to dynamically deforming skin.
  - Used to drive eyebrows, eyelashes, stubble and logical markers.
  - Accelerated by C# Job System and Burst Compiler.

- *Shaders and rendering*
  - Full shader graphs for skin/eyes/teeth/hair as seen in *The Heretic*.
  - Custom pass for cross-material normal buffer blur (tearline).
  - Custom marker-driven occlusion for eyes and teeth.
  
## New improvements

  - Added GPU path for skin deformation and skin attachment calculations.
	- When using GPU path for skin attachment target, EyeRenderer and TeethRenderer need a reference to the SkinAttachmentTarget driving the markers as the transforms aren't updated on CPU anymore
  - Added skin tension to apply wrinkle maps.
  - Custom pass for slight blurring around the eye lids.
  - New shader graphs for eyes and skin.
  - SnappersHeadRenderer now uses texture arrays. If migrating from older version of DHP, use "build texture arrays" to setup the textures properly.


## See also

https://github.com/Unity-Technologies/com.unity.demoteam.digital-human.sample


## Related links

Video: [The Heretic - Unity Short Film](https://www.youtube.com/watch?v=iQZobAhgayA)

Video: [Making of The Heretic (Digital Dragons 2019)](https://www.youtube.com/watch?v=5H9Jo2qjJXs)
