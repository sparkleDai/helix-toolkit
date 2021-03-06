﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PatchGeometryModel3D.cs" company="Helix Toolkit">
//   Copyright (c) 2014 Helix Toolkit contributors
// </copyright>
// <summary>
//
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace HelixToolkit.Wpf.SharpDX
{
    using System.Collections.Generic;
    using System.Windows;
    using System.Linq;
    using global::SharpDX;
    using global::SharpDX.Direct3D;
    using global::SharpDX.Direct3D11;
    using global::SharpDX.DXGI;

    public static class TessellationTechniques
    {
#if TESSELLATION

        /// <summary>
        /// 
        /// </summary>
        private static readonly string[] shading = new[]
        {
            "Solid",
            "Wires",
            "Positions",
            "Normals",
            "TexCoords",
            "Tangents",
            "Colors",
        };

        /// <summary>
        /// Passes available for this Model3D
        /// </summary>
        public static IEnumerable<string> Shading { get { return shading; } }

#endif
    }

    public class PatchGeometryModel3D : MaterialGeometryModel3D
    {
#if TESSELLATION
        /// <summary>
        /// 
        /// </summary>
        public string Shading
        {
            get { return (string)GetValue(ShadingProperty); }
            set { SetValue(ShadingProperty, value); }
        }

        /// <summary>
        /// 
        /// </summary>
        public static readonly DependencyProperty ShadingProperty =
            DependencyProperty.Register("Shading", typeof(string), typeof(PatchGeometryModel3D), new UIPropertyMetadata("Solid", ShadingChanged));

        /// <summary>
        /// 
        /// </summary>
        /// <param name="d"></param>
        /// <param name="e"></param>
        protected static void ShadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var obj = (PatchGeometryModel3D)d;
            if (obj.IsAttached)
            {
                var shadingPass = e.NewValue as string;
                if (TessellationTechniques.Shading.Contains(shadingPass))
                {
                    // --- change the pass
                    obj.shaderPass = obj.effectTechnique.GetPassByName(shadingPass);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public double TessellationFactor
        {
            get { return (double)GetValue(TessellationFactorProperty); }
            set { SetValue(TessellationFactorProperty, value); }
        }

        /// <summary>
        /// 
        /// </summary>
        public static readonly DependencyProperty TessellationFactorProperty =
            DependencyProperty.Register("TessellationFactor", typeof(double), typeof(PatchGeometryModel3D), new UIPropertyMetadata(1.0, TessellationFactorChanged));

        /// <summary>
        /// 
        /// </summary>
        protected static void TessellationFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var obj = (PatchGeometryModel3D)d;
            if (obj.IsAttached)
            {
                obj.vTessellationVariables.Set(new Vector4((float)obj.TessellationFactor, 0, 0, 0));
            }
        }

        public static readonly DependencyProperty ReuseVertexArrayBufferProperty = DependencyProperty.Register("ReuseVertexArrayBuffer", typeof(bool), typeof(PatchGeometryModel3D),
            new PropertyMetadata(false, (s, e) =>
            {
                if (!(bool)e.NewValue)
                {
                    (s as PatchGeometryModel3D).vertexArrayBuffer = null;
                }
            }));

        /// <summary>
        /// Reuse previous vertext array buffer during CreateBuffer. Reduce excessive memory allocation during rapid geometry model changes. 
        /// Example: Repeatly updates textures, or geometries with close number of vertices.
        /// </summary>
        public bool ReuseVertexArrayBuffer
        {
            set
            {
                SetValue(ReuseVertexArrayBufferProperty, value);
            }
            get
            {
                return (bool)GetValue(ReuseVertexArrayBufferProperty);
            }
        }

        private DefaultVertex[] vertexArrayBuffer = null;
        /// <summary>
        /// 
        /// </summary>
        public PatchGeometryModel3D()
        {
            // System.Console.WriteLine();

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="host"></param>
        public override void Attach(IRenderHost host)
        {
            /// --- attach
            renderTechnique = host.RenderTechnique;
            base.Attach(host);

            if (this.Geometry == null
                || this.Geometry.Positions == null || this.Geometry.Positions.Count == 0
                || this.Geometry.Indices == null || this.Geometry.Indices.Count == 0)
                return;
            // --- get variables
            vertexLayout = renderHost.EffectsManager.GetLayout(renderTechnique);
            effectTechnique = effect.GetTechniqueByName(renderTechnique.Name);

            // --- get the pass
            shaderPass = effectTechnique.GetPassByName(Shading);

            /// --- model transformation
            effectTransforms = new EffectTransformVariables(effect);

            /// --- material 
            AttachMaterial();

            // -- get geometry
            var geometry = Geometry as MeshGeometry3D;

            // -- get geometry
            if (geometry != null)
            {
                //throw new HelixToolkitException("Geometry not found!");

                /// --- init vertex buffer
                vertexBuffer = Device.CreateBuffer(BindFlags.VertexBuffer, DefaultVertex.SizeInBytes, CreateDefaultVertexArray(), geometry.Positions.Count);

                /// --- init index buffer
                indexBuffer = Device.CreateBuffer(BindFlags.IndexBuffer, sizeof(int), Geometry.Indices.Array);
            }
            else
            {
                throw new System.Exception("Geometry must not be null");
            }


            ///// --- init instances buffer            
            //this.hasInstances = this.Instances != null;            
            //this.bHasInstances = this.effect.GetVariableByName("bHasInstances").AsScalar();
            //if (this.hasInstances)
            //{                
            //    this.instanceBuffer = Buffer.Create(this.device, this.instanceArray, new BufferDescription(Matrix.SizeInBytes * this.instanceArray.Length, ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0));                            
            //}

            /// --- init tessellation vars
            vTessellationVariables = effect.GetVariableByName("vTessellation").AsVector();
            vTessellationVariables.Set(new Vector4((float)TessellationFactor, 0, 0, 0));

            /// --- flush
            Device.ImmediateContext.Flush();
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Detach()
        {
            Disposer.RemoveAndDispose(ref vTessellationVariables);
            Disposer.RemoveAndDispose(ref shaderPass);
            base.Detach();
        }

        /// <summary>
        /// 
        /// </summary>        
        public override void Update(System.TimeSpan timeSpan)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Render(RenderContext renderContext)
        {
            /// --- check if to render the model
            {
                if (!IsRendering)
                    return;

                if (Geometry == null || this.Geometry.Positions == null || this.Geometry.Positions.Count == 0
                    || this.Geometry.Indices == null || this.Geometry.Indices.Count == 0)
                    return;

                if (Visibility != System.Windows.Visibility.Visible)
                    return;

                if (renderContext.IsShadowPass)
                    if (!IsThrowingShadow)
                        return;
            }

            /// --- set model transform paramerers                         
            effectTransforms.mWorld.SetMatrix(ref modelMatrix);

            /// --- set material props
            if (phongMaterial != null)
            {
                /// --- set material lighting-params      
                effectMaterial.vMaterialDiffuseVariable.Set(phongMaterial.DiffuseColor);
                effectMaterial.vMaterialAmbientVariable.Set(phongMaterial.AmbientColor);
                effectMaterial.vMaterialEmissiveVariable.Set(phongMaterial.EmissiveColor);
                effectMaterial.vMaterialSpecularVariable.Set(phongMaterial.SpecularColor);
                effectMaterial.vMaterialReflectVariable.Set(phongMaterial.ReflectiveColor);
                effectMaterial.sMaterialShininessVariable.Set(phongMaterial.SpecularShininess);

                /// --- set samplers boolean flags
                effectMaterial.bHasDiffuseMapVariable.Set(phongMaterial.DiffuseMap != null && RenderDiffuseMap);
                effectMaterial.bHasNormalMapVariable.Set(phongMaterial.NormalMap != null && RenderNormalMap);
                effectMaterial.bHasDisplacementMapVariable.Set(phongMaterial.DisplacementMap != null && RenderDisplacementMap);

                /// --- set samplers
                if (phongMaterial.DiffuseMap != null)
                {
                    effectMaterial.texDiffuseMapVariable.SetResource(texDiffuseMapView);
                }
                if (phongMaterial.NormalMap != null)
                {
                    effectMaterial.texNormalMapVariable.SetResource(texNormalMapView);
                }
                if (phongMaterial.DisplacementMap != null)
                {
                    effectMaterial.texDisplacementMapVariable.SetResource(texDisplacementMapView);
                }
            }

            /// --- set primitive type
            if (renderTechnique == renderHost.RenderTechniquesManager.RenderTechniques[TessellationRenderTechniqueNames.PNTriangles])
            {
                Device.ImmediateContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.PatchListWith3ControlPoints;
            }
            else if (renderTechnique == renderHost.RenderTechniquesManager.RenderTechniques[TessellationRenderTechniqueNames.PNQuads])
            {
                Device.ImmediateContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.PatchListWith4ControlPoints;
            }
            else
            {
                throw new System.Exception("Technique not supported by PatchGeometryModel3D");
            }

            /// --- set vertex layout
            Device.ImmediateContext.InputAssembler.InputLayout = vertexLayout;

            /// --- set index buffer
            Device.ImmediateContext.InputAssembler.SetIndexBuffer(indexBuffer, Format.R32_UInt, 0);

            /// --- set vertex buffer                
            Device.ImmediateContext.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(vertexBuffer, DefaultVertex.SizeInBytes, 0));

            /// --- apply chosen pass
            shaderPass.Apply(Device.ImmediateContext);

            /// --- render the geometry
            Device.ImmediateContext.DrawIndexed(Geometry.Indices.Count, 0, 0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rayWS"></param>
        /// <param name="hits"></param>
        /// <returns></returns>
        public override bool HitTest(Ray rayWS, ref List<HitTestResult> hits)
        {
            // disable hittesting for patchgeometry for now
            // need to be implemented
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        public override void Dispose()
        {
            Detach();
        }


        private EffectVectorVariable vTessellationVariables;
        private EffectPass shaderPass;

        /// <summary>
        /// Creates a <see cref="T:DefaultVertex[]"/>.
        /// </summary>
        private DefaultVertex[] CreateDefaultVertexArray()
        {
            var geometry = (MeshGeometry3D)this.Geometry;
            var colors = geometry.Colors != null ? geometry.Colors.Array : null;
            var textureCoordinates = geometry.TextureCoordinates != null ? geometry.TextureCoordinates.Array : null;
            var texScale = this.TextureCoodScale;
            var normals = geometry.Normals != null ? geometry.Normals.Array : null;
            var tangents = geometry.Tangents != null ? geometry.Tangents.Array : null;
            var bitangents = geometry.BiTangents != null ? geometry.BiTangents.Array : null;
            var positions = geometry.Positions.Array;
            var vertexCount = geometry.Positions.Count;
            if (!ReuseVertexArrayBuffer || vertexArrayBuffer == null || vertexArrayBuffer.Length < vertexCount)
                vertexArrayBuffer = new DefaultVertex[vertexCount];

            for (var i = 0; i < vertexCount; i++)
            {
                vertexArrayBuffer[i].Position = new Vector4(positions[i], 1f);
                vertexArrayBuffer[i].Color = colors != null ? colors[i] : new Color4(1f, 0f, 0f, 1f);
                vertexArrayBuffer[i].TexCoord = textureCoordinates != null ? texScale * textureCoordinates[i] : Vector2.Zero;
                vertexArrayBuffer[i].Normal = normals != null ? normals[i] : Vector3.Zero;
                vertexArrayBuffer[i].Tangent = tangents != null ? tangents[i] : Vector3.Zero;
                vertexArrayBuffer[i].BiTangent = bitangents != null ? bitangents[i] : Vector3.Zero;
            }

            return vertexArrayBuffer;
        }
#endif
    }
}