﻿using ACadSharp.Entities;
using ACadSharp.IO.Templates;
using CSMath;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ACadSharp.IO.DXF
{
	internal abstract class DxfSectionReaderBase
	{
		public delegate bool ReadEntityDelegate<T>(CadEntityTemplate template, DxfMap map, string subclass = null) where T : Entity;

		/// <summary>
		/// Object reactors, list of handles
		/// </summary>
		public const string ReactorsToken = "{ACAD_REACTORS";

		/// <summary>
		/// Handle for the xdictionary
		/// </summary>
		public const string DictionaryToken = "{ACAD_XDICTIONARY";

		/// <summary>
		/// Block references
		/// </summary>
		public const string BlkRefToken = "{BLKREFS";

		protected readonly IDxfStreamReader _reader;
		protected readonly DxfDocumentBuilder _builder;

		public DxfSectionReaderBase(IDxfStreamReader reader, DxfDocumentBuilder builder)
		{
			this._reader = reader;
			this._builder = builder;
		}

		public abstract void Read();

		protected void readCommonObjectData(out string name, out ulong handle, out ulong? ownerHandle, out ulong? xdictHandle, out List<ulong> reactors)
		{
			name = null;
			handle = 0;
			ownerHandle = null;
			xdictHandle = null;
			reactors = new List<ulong>();

			bool handleNotFound = true;

			//Loop until the common data end
			while (this._reader.DxfCode != DxfCode.Subclass)
			{
				switch (this._reader.Code)
				{
					//Table name
					case 0:
					case 2:
						name = this._reader.ValueAsString;
						break;
					//Handle
					case 5:
					case 105:
						handle = this._reader.ValueAsHandle;
						handleNotFound = false;
						break;
					//Start of application - defined group
					case 102:
						this.readDefinedGroups(out xdictHandle, out reactors);
						continue;
					//Soft - pointer ID / handle to owner BLOCK_RECORD object
					case 330:
						ownerHandle = this._reader.ValueAsHandle;
						break;
					case 71:
					//Number of entries for dimension style table
					case 340:
					//Dimension table has the handles of the styles at the begining
					default:
						this._builder.Notify($"Unhandeled dxf code {this._reader.Code} at line {this._reader.Position}.");
						break;
				}

				this._reader.ReadNext();
			}

			if (handleNotFound) //TODO: Set exception for no handle
				throw new Exception();
		}

		protected void readCommonObjectData(CadTemplate template)
		{
			while (this._reader.DxfCode != DxfCode.Subclass)
			{
				switch (this._reader.Code)
				{
					//object name
					case 0:
						Debug.Assert(template.CadObject.ObjectName == this._reader.ValueAsString);
						break;
					//Handle
					case 5:
						template.CadObject.Handle = this._reader.ValueAsHandle;
						break;
					//Start of application - defined group
					case 102:
						this.readDefinedGroups(template);
						continue;
					//Soft - pointer ID / handle to owner BLOCK_RECORD object
					case 330:
						template.OwnerHandle = this._reader.ValueAsHandle;
						break;
					default:
						this._builder.Notify($"Unhandeled dxf code {this._reader.Code} at line {this._reader.Position}.", NotificationType.None);
						break;
				}

				this._reader.ReadNext();
			}
		}

		protected void readCommonCodes(CadTemplate template, out bool isExtendedData, DxfMap map = null)
		{
			isExtendedData = false;

			switch (this._reader.Code)
			{
				//Handle
				case 5:
					template.CadObject.Handle = this._reader.ValueAsHandle;
					break;
				//Check with mapper
				case 100:
					if (map != null && !map.SubClasses.ContainsKey(this._reader.ValueAsString))
						this._builder.Notify($"[{template.CadObject.ObjectName}] Unidentified subclass {this._reader.ValueAsString}", NotificationType.Warning);
					break;
				//Start of application - defined group
				case 102:
					this.readDefinedGroups(template);
					break;
				//Soft - pointer ID / handle to owner BLOCK_RECORD object
				case 330:
					template.OwnerHandle = this._reader.ValueAsHandle;
					break;
				case 1001:
					isExtendedData = true;
					this.readExtendedData(template.EDataTemplateByAppName);
					break;
				default:
					this._builder.Notify($"[{template.CadObject.ObjectName}] Unhandeled dxf code {this._reader.Code} with value {this._reader.ValueAsString}", NotificationType.None);
					break;
			}
		}

		protected CadEntityTemplate readEntity()
		{
			CadEntityTemplate template = null;

			switch (this._reader.ValueAsString)
			{
				case DxfFileToken.EntityAttribute:
					template = new CadTextEntityTemplate(new AttributeEntity());
					break;
				case DxfFileToken.EntityAttributeDefinition:
					return this.readEntityCodes<AttributeDefinition>(new CadTextEntityTemplate(new AttributeDefinition()), readAttributeDefinition);
				case DxfFileToken.EntityArc:
					return this.readEntityCodes<Arc>(new CadEntityTemplate<Arc>(), readArc);
				case DxfFileToken.EntityCircle:
					return this.readEntityCodes<Circle>(new CadEntityTemplate<Circle>(), readSubclassMap);
				case DxfFileToken.EntityDimension:
					template = new CadDimensionTemplate();
					//return this.readEntityCodes<Dimension>(new CadDimensionTemplate(), readDimension);
					break;
				case DxfFileToken.Entity3DFace:
					return this.readEntityCodes<Face3D>(new CadEntityTemplate<Face3D>(), readSubclassMap);
				case DxfFileToken.EntityEllipse:
					return this.readEntityCodes<Ellipse>(new CadEntityTemplate<Ellipse>(), readSubclassMap);
				case DxfFileToken.EntityLine:
					return this.readEntityCodes<Line>(new CadEntityTemplate<Line>(), readSubclassMap);
				case DxfFileToken.EntityLwPolyline:
					template = new CadLwPolylineTemplate();
					break;
				case DxfFileToken.EntityHatch:
					template = new CadHatchTemplate(new Hatch());
					break;
				case DxfFileToken.EntityInsert:
					template = new CadInsertTemplate(new Insert());
					break;
				case DxfFileToken.EntityMText:
					template = new CadTextEntityTemplate(new MText());
					break;
				case DxfFileToken.EntityMLine:
					template = new CadMLineTemplate(new MLine());
					break;
				case DxfFileToken.EntityPoint:
					template = new CadEntityTemplate(new Point());
					break;
				case DxfFileToken.EntityPolyline:
					template = new CadPolyLineTemplate();
					break;
				case DxfFileToken.EntityRay:
					template = new CadEntityTemplate(new Ray());
					break;
				case DxfFileToken.EndSequence:
					template = new CadEntityTemplate(new Seqend());
					break;
				case DxfFileToken.EntitySolid:
					template = new CadEntityTemplate(new Solid());
					break;
				case DxfFileToken.EntityText:
					return this.readEntityCodes<TextEntity>(new CadTextEntityTemplate(new TextEntity()), readTextEntity);
				case DxfFileToken.EntityVertex:
					template = new CadVertexTemplate();
					break;
				case DxfFileToken.EntityViewport:
					return this.readEntityCodes<Viewport>(new CadViewportTemplate(), this.readViewport);
				case DxfFileToken.EntityXline:
					template = new CadEntityTemplate(new XLine());
					break;
				case DxfFileToken.EntitySpline:
					template = new CadSplineTemplate(new Spline());
					break;
				default:
					this._builder.Notify($"Entity not implemented: {this._reader.ValueAsString}", NotificationType.NotImplemented);
					do
					{
						this._reader.ReadNext();
					}
					while (this._reader.DxfCode != DxfCode.Start);
					return null;
			}

			//Jump the 0 marker
			this._reader.ReadNext();

			this.readCommonObjectData(template);

			while (this._reader.DxfCode == DxfCode.Subclass)
			{
				switch (this._reader.ValueAsString)
				{
					case DxfSubclassMarker.Attribute:
						this.readMapped<AttributeEntity>(template.CadObject, template);
						break;
					case DxfSubclassMarker.AttributeDefinition:
						this.readMapped<AttributeDefinition>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Arc:
						this.readMapped<Arc>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Circle:
						this.readMapped<Circle>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Dimension:
						this.readMapped<Dimension>(template.CadObject, template);
						break;
					case DxfSubclassMarker.AlignedDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionAligned());
						this.readMapped<DimensionAligned>(template.CadObject, template);
						break;
					case DxfSubclassMarker.LinearDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionLinear());
						this.readMapped<DimensionLinear>(template.CadObject, template);
						break;
					case DxfSubclassMarker.RadialDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionRadius());
						this.readMapped<DimensionRadius>(template.CadObject, template);
						break;
					case DxfSubclassMarker.DiametricDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionDiameter());
						this.readMapped<DimensionDiameter>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Angular3PointDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionAngular3Pt());
						this.readMapped<DimensionAngular3Pt>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Angular2LineDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionAngular2Line());
						this.readMapped<DimensionAngular2Line>(template.CadObject, template);
						break;
					case DxfSubclassMarker.OrdinateDimension:
						(template as CadDimensionTemplate).SetDimensionObject(new DimensionOrdinate());
						this.readMapped<DimensionOrdinate>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Ellipse:
						this.readMapped<Ellipse>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Entity:
						this.readMapped<Entity>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Hatch:
						this.readHatch((Hatch)template.CadObject, (CadHatchTemplate)template);
						break;
					case DxfSubclassMarker.Insert:
						this.readMapped<Insert>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Line:
						this.readMapped<Line>(template.CadObject, template);
						break;
					case DxfSubclassMarker.LwPolyline:
						this.readMapped<LwPolyline>(template.CadObject, template);
						break;
					case DxfSubclassMarker.MLine:
						this.readMapped<MLine>(template.CadObject, template);
						break;
					case DxfSubclassMarker.MText:
						this.readMapped<MText>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Point:
						this.readMapped<Point>(template.CadObject, template);
						break;
					case DxfSubclassMarker.PolyfaceMesh:
						this._builder.Notify($"dxf entity subclass not implemented {this._reader.ValueAsString}", NotificationType.NotImplemented);
						while (this._reader.DxfCode != DxfCode.Start)
							this._reader.ReadNext();
						return null;
					case DxfSubclassMarker.Polyline:
						(template as CadPolyLineTemplate).SetPolyLineObject(new Polyline2D());
						this.readMapped<Polyline2D>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Polyline3d:
						(template as CadPolyLineTemplate).SetPolyLineObject(new Polyline3D());
						this.readMapped<Polyline3D>(template.CadObject, template);
						break;
					case DxfSubclassMarker.PolylineVertex:
						(template as CadVertexTemplate).SetVertexObject(new Vertex2D());
						this.readMapped<Vertex2D>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Polyline3dVertex:
						(template as CadVertexTemplate).SetVertexObject(new Vertex3D());
						this.readMapped<Vertex3D>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Ray:
						this.readMapped<Ray>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Text:
						this.readMapped<TextEntity>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Trace:
						this.readMapped<Solid>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Vertex:
						this.readMapped<Vertex>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Viewport:
						this.readMapped<Viewport>(template.CadObject, template);
						break;
					case DxfSubclassMarker.XLine:
						this.readMapped<XLine>(template.CadObject, template);
						break;
					case DxfSubclassMarker.Spline:
						this.readMapped<Spline>(template.CadObject, template);
						break;
					default:
						this._builder.Notify($"Unhandeled dxf entity subclass {this._reader.ValueAsString}");
						while (this._reader.DxfCode != DxfCode.Start)
							this._reader.ReadNext();
						break;
				}
			}

			return template;
		}

		protected CadEntityTemplate readEntityCodes<T>(CadEntityTemplate template, ReadEntityDelegate<T> readEntity)
			where T : Entity
		{
			this._reader.ReadNext();

			DxfMap map = DxfMap.Create<T>();

			while (this._reader.DxfCode != DxfCode.Start)
			{
				if (!readEntity(template, map))
				{
					this.readCommonEntityCodes(template, out bool isExtendedData, map);
					if (isExtendedData)
						continue;
				}

				if (this._reader.DxfCode != DxfCode.Start)
					this._reader.ReadNext();
			}

			return template;
		}

		protected void readCommonEntityCodes(CadEntityTemplate template, out bool isExtendedData, DxfMap map = null)
		{
			isExtendedData = false;
			switch (this._reader.Code)
			{
				case 6:
					template.LineTypeName = this._reader.ValueAsString;
					break;
				case 8:
					template.LayerName = this._reader.ValueAsString;
					break;
				case 347:
					template.MaterialHandle = this._reader.ValueAsHandle;
					break;
				default:
					if (!this.tryAssignCurrentValue(template.CadObject, map.SubClasses[DxfSubclassMarker.Entity]))
					{
						this.readCommonCodes(template, out isExtendedData, map);
					}
					break;
			}
		}

		private bool readArc(CadEntityTemplate template, DxfMap map, string subclass = null)
		{
			switch (this._reader.Code)
			{
				default:
					if (!this.tryAssignCurrentValue(template.CadObject, map.SubClasses[DxfSubclassMarker.Arc]))
					{
						return this.readSubclassMap(template, map, DxfSubclassMarker.Circle);
					}
					return true;
			}
		}

		private bool readAttributeDefinition(CadEntityTemplate template, DxfMap map, string subclass = null)
		{
			DxfClassMap emap = map.SubClasses[template.CadObject.SubclassMarker];
			CadTextEntityTemplate tmp = template as CadTextEntityTemplate;

			switch (this._reader.Code)
			{
				//TODO: Implement multiline attribute def codes
				case 44:
				case 46:
				case 101:
					return true;
				default:
					if (!this.tryAssignCurrentValue(template.CadObject, emap))
					{
						return this.readTextEntity(template, map, DxfSubclassMarker.Text);
					}
					return true;
			}
		}

		private bool readTextEntity(CadEntityTemplate template, DxfMap map, string subclass = null)
		{
			string mapName = string.IsNullOrEmpty(subclass) ? template.CadObject.SubclassMarker : subclass;
			CadTextEntityTemplate tmp = template as CadTextEntityTemplate;

			switch (this._reader.Code)
			{
				case 7:
					tmp.StyleName = this._reader.ValueAsString;
					return true;
				default:
					return this.tryAssignCurrentValue(template.CadObject, map.SubClasses[mapName]);
			}
		}

		private bool readDimension(CadEntityTemplate template, DxfMap map, string subclass = null)
		{
			DxfClassMap dimMap = map.SubClasses[DxfSubclassMarker.Dimension];

			switch (this._reader.Code)
			{
				default:
					return this.tryAssignCurrentValue(template.CadObject, dimMap);
			}
		}

		private bool readViewport(CadEntityTemplate template, DxfMap map, string subclass = null)
		{
			CadViewportTemplate tmp = template as CadViewportTemplate;

			switch (this._reader.Code)
			{
				//Undocumented
				case 67:
				case 68:
					return true;
				case 69:
					tmp.ViewportId = this._reader.ValueAsShort;
					return true;
				case 348:
					tmp.VisualStyleHandle = this._reader.ValueAsHandle;
					return true;
				default:
					return this.tryAssignCurrentValue(template.CadObject, map.SubClasses[DxfSubclassMarker.Viewport]);
			}
		}

		private bool readSubclassMap(CadEntityTemplate template, DxfMap map, string subclass = null)
		{
			string mapName = string.IsNullOrEmpty(subclass) ? template.CadObject.SubclassMarker : subclass;

			switch (this._reader.Code)
			{
				default:
					return this.tryAssignCurrentValue(template.CadObject, map.SubClasses[mapName]);
			}
		}

		protected void readMapped<T>(CadObject cadObject, CadTemplate template)
			where T : CadObject
		{
			DxfClassMap map = DxfClassMap.Create<T>();

			Debug.Assert(map.Name == this._reader.ValueAsString);
			this._reader.ReadNext();

			while (this._reader.DxfCode != DxfCode.Start
				&& this._reader.DxfCode != DxfCode.Subclass)
			{
				//Check for an extended data code
				if (this._reader.DxfCode == DxfCode.ExtendedDataRegAppName)
				{
					this.readExtendedData(template.EDataTemplateByAppName);
					continue;
				}
				else if (this._reader.DxfCode >= DxfCode.ExtendedDataAsciiString)
				{
					this._builder.Notify($"Extended data should start witth : {DxfCode.ExtendedDataRegAppName}");
					this._reader.ReadNext();
					continue;
				}
				else if (this._reader.DxfCode == DxfCode.ControlString)
				{
					if (!template.CheckDxfCode(this._reader.Code, this._reader.Value))
					{
						this.readDefinedGroups(template);
					}
					else
					{
						this._reader.ReadNext();
					}

					continue;
				}

				if (!map.DxfProperties.TryGetValue(this._reader.Code, out DxfProperty dxfProperty))
				{
					if (!template.CheckDxfCode(this._reader.Code, this._reader.Value))
						this._builder.Notify($"Dxf code {this._reader.Code} not found in map for {typeof(T)} | value : {this._reader.ValueAsString}");

					this._reader.ReadNext();
					continue;
				}

				if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Handle))
				{
					if (!template.AddHandle(this._reader.Code, this._reader.ValueAsHandle))
						this._builder.Notify($"Dxf referenced code {this._reader.Code} not implemented in the {template.GetType().Name} for {typeof(T)} | value : {this._reader.ValueAsHandle}");
				}
				else if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Name))
				{
					if (!template.AddName(this._reader.Code, this._reader.ValueAsString))
						this._builder.Notify($"Dxf named referenced code {this._reader.Code} not implemented in the {template.GetType().Name} for {typeof(T)} | value : {this._reader.ValueAsString}");
				}
				else if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Count))
				{
					//Do nothing just marks the amount
				}
				else if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Unprocess) || dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Ignored))
				{
					this._reader.ReadNext();
					continue;
				}
				else
				{
					object value = this._reader.Value;

					if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.IsAngle))
					{
						value = (double)value * MathUtils.DegToRad;
					}

					switch (this._reader.GroupCodeValue)
					{
						case GroupCodeValueType.String:
						case GroupCodeValueType.Point3D:
						case GroupCodeValueType.Double:
						case GroupCodeValueType.Int16:
						case GroupCodeValueType.Int32:
						case GroupCodeValueType.Int64:
						case GroupCodeValueType.Chunk:
						case GroupCodeValueType.Bool:
							dxfProperty.SetValue(this._reader.Code, cadObject, value);
							break;
						case GroupCodeValueType.Comment:
							this._builder.Notify($"Comment in the file:  {this._reader.ValueAsString}");
							break;
						case GroupCodeValueType.Handle:
						case GroupCodeValueType.ObjectId:
						case GroupCodeValueType.None:
						default:
							this._builder.Notify($"Group Code not handled {this._reader.GroupCodeValue} for {typeof(T)}, code : {this._reader.Code} | value : {this._reader.ValueAsString}");
							break;
					}
				}

				this._reader.ReadNext();
			}
		}

		protected void readExtendedData(Dictionary<string, ExtendedData> edata)
		{
			ExtendedData extendedData = new ExtendedData();
			edata.Add(this._reader.ValueAsString, extendedData);

			this._reader.ReadNext();

			while (this._reader.DxfCode >= DxfCode.ExtendedDataAsciiString)
			{
				if (this._reader.DxfCode == DxfCode.ExtendedDataRegAppName)
				{
					this.readExtendedData(edata);
					break;
				}

				extendedData.Data.Add(new ExtendedDataRecord(this._reader.DxfCode, this._reader.Value));

				this._reader.ReadNext();
			}
		}

		protected void readHatch(Hatch hatch, CadHatchTemplate template)
		{
			bool isFirstSeed = true;
			XY seedPoint = new XY();
			DxfClassMap map = DxfClassMap.Create<Hatch>();

			//Jump sublcass
			this._reader.ReadNext();

			while (this._reader.DxfCode != DxfCode.Start)
			{
				map.DxfProperties.TryGetValue(this._reader.Code, out DxfProperty dxfProperty);

				switch (this._reader.Code)
				{
					//TODO: Check hatch undocumented codes
					case 43:
					case 44:
					case 45:
					case 46:
					case 49:
					case 53:
					case 79:
					case 90:
						break;
					case 2:
						template.HatchPatternName = this._reader.ValueAsString;
						break;
					case 10:
						seedPoint = new XY(this._reader.ValueAsDouble, seedPoint.Y);
						break;
					case 20:
						if (!isFirstSeed)
						{
							seedPoint = new XY(seedPoint.X, this._reader.ValueAsDouble);
							hatch.SeedPoints.Add(seedPoint);
						}
						break;
					case 30:
						hatch.Elevation = this._reader.ValueAsDouble;
						isFirstSeed = false;
						break;
					case 78:    //Number of pattern definition lines
						break;
					case 91:    //Number of boundary paths (loops)
						this.readLoops(hatch, template, this._reader.ValueAsInt);
						continue;
					case 98:    //Number of seed points
						break;
					case 450:
						hatch.GradientColor.Enabled = this._reader.ValueAsBool;
						break;
					case 451:
						hatch.GradientColor.Reserved = this._reader.ValueAsInt;
						break;
					case 452:
						hatch.GradientColor.IsSingleColorGradient = this._reader.ValueAsBool;
						break;
					case 453:
						//Number of colors
						break;
					case 460:
						hatch.GradientColor.Angle = this._reader.ValueAsDouble;
						break;
					case 461:
						hatch.GradientColor.Shift = this._reader.ValueAsDouble;
						break;
					case 462:
						hatch.GradientColor.ColorTint = this._reader.ValueAsDouble;
						break;
					case 463:
						GradientColor gradient = new GradientColor();
						gradient.Value = this._reader.ValueAsDouble;
						hatch.GradientColor.Colors.Add(gradient);
						break;
					case 63:
						GradientColor colorByIndex = hatch.GradientColor.Colors.LastOrDefault();
						if (colorByIndex != null)
						{
							colorByIndex.Color = new Color((short)this._reader.ValueAsUShort);
						}
						break;
					case 421:
						GradientColor colorByRgb = hatch.GradientColor.Colors.LastOrDefault();
						if (colorByRgb != null)
						{
							//TODO: Hatch assign color by true color
							//TODO: Is always duplicated by 63, is it needed??
							//colorByRgb.Color = new Color(this._reader.LastValueAsShort);
						}
						break;
					case 470:
						hatch.GradientColor.Name = this._reader.ValueAsString;
						break;
					default:
						if (dxfProperty != null)
						{
							dxfProperty.SetValue(hatch, this._reader.Value);
							break;
						}
						else if (this._reader.DxfCode >= DxfCode.ExtendedDataAsciiString)
						{
							this.readExtendedData(template.EDataTemplateByAppName);
							continue;
						}
						this._builder.Notify($"Unhandeled dxf code : {this._reader.Code} with value : {this._reader.Value} for subclass {DxfSubclassMarker.Hatch}");
						break;
				}

				this._reader.ReadNext();
			}
		}

		private void readLoops(Hatch hatch, CadHatchTemplate template, int count)
		{
			if (this._reader.Code == 91)
				this._reader.ReadNext();

			for (int i = 0; i < count; i++)
			{
				if (this._reader.Code != 92)
				{
					this._builder.Notify($"Boundary path should start with code 92 but was {this._reader.Code}");
					break;
				}

				CadHatchTemplate.CadBoundaryPathTemplate path = this.readLoop();
				if (path != null)
					template.PathTempaltes.Add(path);
			}
		}

		private CadHatchTemplate.CadBoundaryPathTemplate readLoop()
		{
			CadHatchTemplate.CadBoundaryPathTemplate template = new CadHatchTemplate.CadBoundaryPathTemplate();
			template.Path.Flags = (BoundaryPathFlags)this._reader.ValueAsInt;

			if (template.Path.Flags.HasFlag(BoundaryPathFlags.Polyline))
			{
				Hatch.BoundaryPath.Edge pl = new Hatch.BoundaryPath.Polyline();
				this._builder.Notify($"Hatch.BoundaryPath.Polyline not implemented", NotificationType.Error);

				return null;
			}
			else
			{
				this._reader.ReadNext();

				if (this._reader.Code != 93)
				{
					this._builder.Notify($"Edge Boundary path should start with code 93 but was {this._reader.Code}");
					return null;
				}

				int edges = this._reader.ValueAsInt;
				this._reader.ReadNext();

				for (int i = 0; i < edges; i++)
				{
					var edge = this.readEdge();
					if (edge != null)
						template.Path.Edges.Add(edge);
				}
			}

			bool end = false;
			while (!end)
			{
				switch (this._reader.Code)
				{
					//Number of source boundary objects
					case 97:
						break;
					case 330:
						template.Handles.Add(this._reader.ValueAsHandle);
						break;
					default:
						end = true;
						continue;
				}

				this._reader.ReadNext();
			}

			return template;
		}

		private void readPolylineBoundary()
		{

		}

		private Hatch.BoundaryPath.Edge readEdge()
		{
			if (this._reader.Code != 72)
			{
				this._builder.Notify($"Edge Boundary path should should define the type with code 72 but was {this._reader.Code}");
				return null;
			}

			Hatch.BoundaryPath.EdgeType type = (Hatch.BoundaryPath.EdgeType)this._reader.ValueAsInt;
			this._reader.ReadNext();

			switch (type)
			{
				case Hatch.BoundaryPath.EdgeType.Line:
					Hatch.BoundaryPath.Line line = new Hatch.BoundaryPath.Line();
					while (true)
					{
						switch (this._reader.Code)
						{
							case 10:
								line.Start = new XY(this._reader.ValueAsDouble, line.Start.Y);
								break;
							case 20:
								line.Start = new XY(line.Start.X, this._reader.ValueAsDouble);
								break;
							case 11:
								line.End = new XY(this._reader.ValueAsDouble, line.End.Y);
								break;
							case 21:
								line.End = new XY(line.End.X, this._reader.ValueAsDouble);
								break;
							default:
								return line;
						}

						this._reader.ReadNext();
					}
				case Hatch.BoundaryPath.EdgeType.CircularArc:
					Hatch.BoundaryPath.Arc arc = new Hatch.BoundaryPath.Arc();
					while (true)
					{
						switch (this._reader.Code)
						{
							case 10:
								arc.Center = new XY(this._reader.ValueAsDouble, arc.Center.Y);
								break;
							case 20:
								arc.Center = new XY(arc.Center.X, this._reader.ValueAsDouble);
								break;
							case 40:
								arc.Radius = this._reader.ValueAsDouble;
								break;
							case 50:
								arc.StartAngle = this._reader.ValueAsDouble;
								break;
							case 51:
								arc.EndAngle = this._reader.ValueAsDouble;
								break;
							case 73:
								arc.CounterClockWise = this._reader.ValueAsBool;
								break;
							default:
								return arc;
						}

						this._reader.ReadNext();
					}
				case Hatch.BoundaryPath.EdgeType.EllipticArc:
					Hatch.BoundaryPath.Ellipse ellipse = new Hatch.BoundaryPath.Ellipse();
					while (true)
					{
						switch (this._reader.Code)
						{
							case 10:
								ellipse.Center = new XY(this._reader.ValueAsDouble, ellipse.Center.Y);
								break;
							case 20:
								ellipse.Center = new XY(ellipse.Center.X, this._reader.ValueAsDouble);
								break;
							case 11:
								ellipse.MajorAxisEndPoint = new XY(this._reader.ValueAsDouble, ellipse.Center.Y);
								break;
							case 21:
								ellipse.MajorAxisEndPoint = new XY(ellipse.Center.X, this._reader.ValueAsDouble);
								break;
							case 40:
								ellipse.Radius = this._reader.ValueAsDouble;
								break;
							case 50:
								ellipse.StartAngle = this._reader.ValueAsDouble;
								break;
							case 51:
								ellipse.EndAngle = this._reader.ValueAsDouble;
								break;
							case 73:
								ellipse.CounterClockWise = this._reader.ValueAsBool;
								break;
							default:
								return ellipse;
						}

						this._reader.ReadNext();
					}
				case Hatch.BoundaryPath.EdgeType.Spline:
					Hatch.BoundaryPath.Spline spline = new Hatch.BoundaryPath.Spline();
					int nKnots = 0;
					int nCtrlPoints = 0;
					int nFitPoints = 0;

					XYZ controlPoint = new XYZ();
					XY fitPoint = new XY();

					while (true)
					{
						switch (this._reader.Code)
						{
							case 10:
								controlPoint = new XYZ(this._reader.ValueAsDouble, 0, 1);
								break;
							case 20:
								controlPoint = new XYZ(controlPoint.X, this._reader.ValueAsDouble, controlPoint.Z);
								spline.ControlPoints.Add(controlPoint);
								break;
							case 11:
								fitPoint = new XY(this._reader.ValueAsDouble, 0);
								break;
							case 21:
								fitPoint = new XY(fitPoint.X, this._reader.ValueAsDouble);
								spline.FitPoints.Add(fitPoint);
								break;
							case 42:
								var last = spline.ControlPoints[spline.ControlPoints.Count - 1];
								spline.ControlPoints[spline.ControlPoints.Count - 1] = new XYZ(last.X, last.Y, this._reader.ValueAsDouble);
								break;
							case 12:
								spline.StartTangent = new XY(this._reader.ValueAsDouble, spline.StartTangent.Y);
								break;
							case 22:
								spline.StartTangent = new XY(spline.StartTangent.X, this._reader.ValueAsDouble);
								break;
							case 13:
								spline.EndTangent = new XY(this._reader.ValueAsDouble, spline.EndTangent.Y);
								break;
							case 23:
								spline.EndTangent = new XY(spline.EndTangent.X, this._reader.ValueAsDouble);
								break;
							case 94:
								spline.Degree = this._reader.ValueAsInt;
								break;
							case 73:
								spline.Rational = this._reader.ValueAsBool;
								break;
							case 74:
								spline.Periodic = this._reader.ValueAsBool;
								break;
							case 95:
								nKnots = this._reader.ValueAsInt;
								break;
							case 96:
								nCtrlPoints = this._reader.ValueAsInt;
								break;
							case 97:
								nFitPoints = this._reader.ValueAsInt;
								break;
							case 40:
								spline.Knots.Add(this._reader.ValueAsDouble);
								break;
							default:
								return spline;
						}

						this._reader.ReadNext();
					}
			}

			return null;
		}

		private void readDefinedGroups(CadTemplate template)
		{
			this.readDefinedGroups(out ulong? xdict, out List<ulong> reactorsHandles);

			template.XDictHandle = xdict;
			template.ReactorsHandles = reactorsHandles;
		}

		private void readDefinedGroups(out ulong? xdictHandle, out List<ulong> reactors)
		{
			xdictHandle = null;
			reactors = new List<ulong>();

			switch (this._reader.ValueAsString)
			{
				case DxfSectionReaderBase.DictionaryToken:
					this._reader.ReadNext();
					xdictHandle = this._reader.ValueAsHandle;
					this._reader.ReadNext();
					Debug.Assert(this._reader.DxfCode == DxfCode.ControlString);
					break;
				case DxfSectionReaderBase.ReactorsToken:
				case DxfSectionReaderBase.BlkRefToken:
				default:
					do
					{
						this._reader.ReadNext();
					}
					while (this._reader.DxfCode != DxfCode.ControlString);
					break;
			}
			this._reader.ReadNext();
		}

		protected bool tryAssignCurrentValue(CadObject cadObject, DxfClassMap map)
		{
			try
			{
				//Use this method only if the value is not a link between objects
				if (map.DxfProperties.TryGetValue(this._reader.Code, out DxfProperty dxfProperty))
				{
					if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Handle)
						|| dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Name)
						|| dxfProperty.ReferenceType.HasFlag(DxfReferenceType.Count))
					{
						return false;
					}

					object value = this._reader.Value;

					if (dxfProperty.ReferenceType.HasFlag(DxfReferenceType.IsAngle))
					{
						value = (double)value * MathUtils.DegToRad;
					}

					dxfProperty.SetValue(this._reader.Code, cadObject, value);

					return true;
				}
			}
			catch (Exception ex)
			{
				if (!_builder.Configuration.Failsafe)
				{
					throw ex;
				}
				else
				{
					this._builder.Notify("An error occurred while assiging a property using mapper", NotificationType.Error, ex);
				}
			}

			return false;
		}
	}
}
