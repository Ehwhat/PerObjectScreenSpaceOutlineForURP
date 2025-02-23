# Per-Object Screen Space Outline for URP RenderGraph (Unity 6+)

This is a small render feature for drawing screen space outlines and infills using RenderGraph. Should work for Unity 6.1+ using the latest RenderGraph.



![Preview Screenshot](https://i.ibb.co/7dD23B08/image.png)


## Usage

1. Create a new OutlineDefinition asset (Assets > Create > Ehlib > Outline Definition).
2. Assign the OutlineDefinition to an Outline component.
3. Add the Outline component to a GameObject. (All renderers in the GetComponentInChildren will be rendered with the outline effect)
4. Add the ScreenSpaceOutlineRenderFeature to the renderer.
