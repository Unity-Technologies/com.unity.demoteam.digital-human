# Package: com.unity.demoteam.digital-human

Library of tech features used to realize the digital human from *The Heretic*.


## Requirements

- Unity 2019.3.12f1 +
- HDRP 7.3.1 +


## Features

- *Facial animation systems*
  - Tools for 4D clip import and processing. <sub>(When we say 4D, we mean a sequence of meshes captured over time.)</sub>
  - 4D clip rendering with timeline integration.
  - 4D frame fitting allowing detail injection from facial rig.
  - Integration of facial rig from Snappers.
  - Pose facial rig directly in Unity

- *Skin attachment system*
  - Drive meshes and transforms in relation to dynamically deforming skin.
  - Used to drive eyebrows, eyelashes, stubble and logical markers.
  - Accelerated by C# Job System and Burst Compiler.

- *Shaders and rendering*
  - Full shader graphs for skin/eyes/teeth/hair as seen in *The Heretic*.
  - Custom pass for cross-material normal buffer blur (tearline).
  - Custom marker-driven occlusion for eyes and teeth.


## Usage

Declare the package and its dependencies as git dependencies in `Packages/manifest.json`:

```
"dependencies": {
    "com.unity.demoteam.digital-human": "https://github.com/Unity-Technologies/com.unity.demoteam.digital-human.git",
    "com.unity.demoteam.attributes": "https://github.com/Unity-Technologies/com.unity.demoteam.attributes.git",
    ...
}
```


## See also

https://github.com/Unity-Technologies/com.unity.demoteam.digital-human.sample


## Related links

Video: [The Heretic - Unity Short Film](https://www.youtube.com/watch?v=iQZobAhgayA)

Video: [Making of The Heretic (Digital Dragons 2019)](https://www.youtube.com/watch?v=5H9Jo2qjJXs)
