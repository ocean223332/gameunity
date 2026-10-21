"""Run with Blender --background --python this.py -- --boss FILE --weapon FILE --output DIR.
Original inputs are read-only. Generated FBX files use metres, Unity +Z forward.
"""
import argparse, json, math, sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def load(path):
    bpy.ops.import_scene.gltf(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(meshes) != 1:
        raise RuntimeError('Expected one source mesh, got %d' % len(meshes))
    obj = meshes[0]
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj

def textures(obj, directory, prefix):
    directory.mkdir(parents=True, exist_ok=True)
    material = obj.data.materials[0]
    shader = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    def image_for(socket):
        links = shader.inputs[socket].links
        if not links:
            return None
        node = links[0].from_node
        if node.type == 'TEX_IMAGE':
            return node.image
        for inp in node.inputs:
            if inp.is_linked and inp.links[0].from_node.type == 'TEX_IMAGE':
                return inp.links[0].from_node.image
        return None
    for label, socket in [('BaseColor','Base Color'), ('Normal','Normal')]:
        img = image_for(socket)
        if img:
            if max(img.size) > 2048:
                ratio = 2048/max(img.size)
                img.scale(round(img.size[0]*ratio), round(img.size[1]*ratio))
            img.filepath_raw = str(directory / (prefix+'_'+label+'.png'))
            img.file_format = 'PNG'
            img.save()
    # glTF ORM: R occlusion, G roughness, B metallic. Unity Lit: R metallic, A smoothness.
    img = image_for('Roughness') or image_for('Metallic')
    if img:
        if max(img.size) > 2048:
            ratio=2048/max(img.size);img.scale(round(img.size[0]*ratio),round(img.size[1]*ratio))
        pixels = np.empty(len(img.pixels), dtype=np.float32)
        img.pixels.foreach_get(pixels)
        pixels=pixels.reshape((-1,4)); packed=np.zeros_like(pixels)
        packed[:,0]=pixels[:,2];packed[:,3]=1-pixels[:,1]
        out=bpy.data.images.new(prefix+'_MetallicSmoothness',width=img.size[0],height=img.size[1],alpha=True)
        out.colorspace_settings.name='Non-Color';out.pixels.foreach_set(packed.ravel())
        out.filepath_raw=str(directory/(prefix+'_MetallicSmoothness.png'));out.file_format='PNG';out.save()

def export(path, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH','ARMATURE'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,
        bake_anim=False,use_mesh_modifiers=True,path_mode='AUTO')

def geometry(obj, path, arm=None):
    obj.data.calc_loop_triangles()
    coords=[];normals=[];uvs=[];triangles=[];weights=[]
    # The source is Blender Z-up facing -Y; Unity is Y-up facing +Z, left bones -X.
    names=[b.name for b in arm.data.bones] if arm else []
    for triangle in obj.data.loop_triangles:
        for loop_index in reversed(triangle.loops):
            loop=obj.data.loops[loop_index];v=obj.data.vertices[loop.vertex_index]
            co=obj.matrix_world@v.co;normal=obj.matrix_world.to_3x3()@v.normal
            coords.append(dict(x=-co.x,y=co.z,z=-co.y));normals.append(dict(x=-normal.x,y=normal.z,z=-normal.y))
            uv=obj.data.uv_layers.active.data[loop_index].uv;uvs.append(dict(x=uv.x,y=uv.y));triangles.append(len(triangles))
            if arm:
                groups=sorted([(names.index(obj.vertex_groups[g.group].name),g.weight) for g in v.groups if obj.vertex_groups[g.group].name in names and g.weight>0],key=lambda p:-p[1])[:4]
                total=sum(w for _,w in groups);weights.append({'indices':[g for g,_ in groups]+[0]*(4-len(groups)),'weights':[w/total for _,w in groups]+[0]*(4-len(groups))})
    bones=[]
    if arm:
        for b in arm.data.bones:
            head=arm.matrix_world@b.head_local
            parent=arm.matrix_world@b.parent.head_local if b.parent else Vector()
            pos=head-parent
            bones.append({'name':b.name,'parent':names.index(b.parent.name) if b.parent else -1,'position':dict(x=-pos.x,y=pos.z,z=-pos.y)})
    data={'positions':coords,'normals':normals,'uv':uvs,'triangles':triangles,'bones':bones,'weights':weights}
    path.write_text(json.dumps(data,separators=(',',':')))
def rig_body(path, out):
    clear();obj=load(path);obj.name='CommanderBody'
    obj.rotation_mode='XYZ'
    obj.rotation_euler.z=math.pi/2
    obj.scale=(1.85/obj.dimensions.z,)*3
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    textures(obj,out/'Textures','Commander')
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=.0001);bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    armdata=bpy.data.armatures.new('CommanderSkeleton');arm=bpy.data.objects.new('CommanderRig',armdata)
    bpy.context.collection.objects.link(arm);bpy.context.view_layer.objects.active=arm
    obj.select_set(False);arm.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
    joints={
      'Hips':((0,0,.94),(0,0,1.08),None),
      'Spine':((0,0,1.08),(0,0,1.26),'Hips'),
      'Chest':((0,0,1.26),(0,0,1.42),'Spine'),
      'UpperChest':((0,0,1.42),(0,0,1.53),'Chest'),
      'Neck':((0,0,1.53),(0,-.015,1.65),'UpperChest'),
      'Head':((0,-.015,1.65),(0,-.015,1.83),'Neck')}
    for side,sign in [('Left',1),('Right',-1)]:
        def p(x,y,z): return (x*sign,y,z)
        joints.update({
          side+'Shoulder':(p(.055,0,1.47),p(.235,0,1.46),'UpperChest'),
          side+'UpperArm':(p(.235,0,1.46),p(.345,-.012,1.18),side+'Shoulder'),
          side+'LowerArm':(p(.345,-.012,1.18),p(.455,-.035,.94),side+'UpperArm'),
          side+'Hand':(p(.455,-.035,.94),p(.49,-.045,.83),side+'LowerArm'),
          side+'UpperLeg':(p(.115,0,.95),p(.13,-.025,.54),'Hips'),
          side+'LowerLeg':(p(.13,-.025,.54),p(.13,0,.13),side+'UpperLeg'),
          side+'Foot':(p(.13,0,.13),p(.13,-.16,.075),side+'LowerLeg'),
          side+'Toes':(p(.13,-.16,.075),p(.13,-.24,.065),side+'Foot')})
    for name,(head,tail,parent) in joints.items():
        b=armdata.edit_bones.new(name);b.head=head;b.tail=tail
        if parent: b.parent=armdata.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT');obj.select_set(True);arm.select_set(True)
    bpy.context.view_layer.objects.active=arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    unweighted=[v.index for v in obj.data.vertices if sum(g.weight for g in v.groups)<.001]
    if unweighted: raise RuntimeError('%d vertices have no weights'%len(unweighted))
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    geometry(obj,out/'CommanderGeometry.json',arm)
    export(out/'Commander_Rig.fbx',[arm,obj])
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'Commander_Rig.blend'))
    return {'bones':len(joints),'vertices':len(obj.data.vertices),'unweighted':len(unweighted),'height':obj.dimensions.z}

def weapon(path,out):
    clear();obj=load(path);obj.name='RPG18'
    before=sum(len(p.vertices)-2 for p in obj.data.polygons)
    modifier=obj.modifiers.new('GameMesh','DECIMATE');modifier.ratio=24000/before;modifier.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Blender -Y maps to Unity +Z: tube axis forward, centred for attachment.
    center=sum((Vector(c) for c in obj.bound_box),Vector())/8
    for v in obj.data.vertices:v.co-=center
    obj.scale=(1.05/obj.dimensions.y,)*3
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    textures(obj,out/'Textures','RPG18')
    geometry(obj,out/'RPG18Geometry.json')
    export(out/'RPG18.fbx',[obj])
    return {'source_triangles':before,'game_triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'length':obj.dimensions.y}

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--boss',type=Path,required=True);parser.add_argument('--weapon',type=Path,required=True);parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:]);args.output.mkdir(parents=True,exist_ok=True)
    result={'commander':rig_body(args.boss,args.output),'weapon':weapon(args.weapon,args.output)}
    (args.output/'asset-report.json').write_text(json.dumps(result,indent=2));print('BOSS_ASSETS',json.dumps(result))




