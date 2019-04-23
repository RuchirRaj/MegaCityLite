# Performance considerations

Here is a checklist you can use to get MegaCity to run better on your machine:

* Unity 2019.1.0b7 or a more recent version of 2019.1 is required. The project is currently not compatible with 2019.2.
* In the Edit menu, Project Settings, Quality, select Medium or Low. Insane can be very taxing on the graphics card. You can also tweak the LOD Bias (lower it to trade quality for speed), more info here: https://docs.unity3d.com/Manual/class-QualitySettings.html
* In the Jobs menu, Burst, make sure that Enable Compilation is toggled on and that Safety Checks isn’t.
* In the Jobs menu, Leak Detection, make sure that Off is selected. Leak detection uses small GC allocations for debugging purposes and those will slow everything down.
* Look for the GameObject called "PlayerCam" under "000_Scene", and lower the streaming radiuses. Everything outside of the Streaming Out radius has its high LOD streamed out in playmode, everything inside the In radius has it streamed in.

Building a player:
* In Project Settings, Player, Backend, select IL2CPP. (make sure Windows Build Support is installed)
* Build in x86_64 (x86 isn’t supported) and ensure Development Build isn’t selected.

Working with SubScenes:
* SubScenes in edit mode are a lot heavier, only edit a few at a time.
* Disable the selection outline in the Gizmos menus at the top of the scene view window.
* Disable auto-save prefab, some can take a long time to propagate changes.