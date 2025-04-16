UndertaleGameObject DefineGameObject(UndertaleData utdata, string name, bool throwIfExists = false) {
    var objDef = utdata.GameObjects.ByName(name);

    if(objDef == null) {
        objDef = new UndertaleGameObject() {
            Name = utdata.Strings.MakeString(name)
        };
        utdata.GameObjects.Add(objDef);
    } else if(throwIfExists) {
        throw new ScriptException($"Game object with name '{name}' already exists");
    }

    return objDef;
}
UndertaleRoom.GameObject DefineRoomGameObject(UndertaleData utdata, int roomOrderIndex, UndertaleGameObject objDef, bool throwIfExists = false) {
    return DefineRoomGameObject(utdata, utdata.GeneralInfo.RoomOrder[roomOrderIndex].Resource, objDef, throwIfExists);
}

UndertaleRoom.GameObject DefineRoomGameObject(UndertaleData utdata, UndertaleRoom room, UndertaleGameObject objDef, bool throwIfExists = false) {
    UndertaleRoom.GameObject obj = null;

    foreach(var o in room.GameObjects) {
        if(o.ObjectDefinition == objDef) {
            obj = o;
            break;
        }
    }

    if(obj == null) {
        obj = new UndertaleRoom.GameObject() {
            InstanceID = utdata.GeneralInfo.LastObj++,
            ObjectDefinition = objDef,
        };
        room.GameObjects.Add(obj);
    } else if(throwIfExists) {
        throw new ScriptException($"Game object for '{objDef.Name?.Content}' already exists in room '{room.Name?.Content}' with id '{obj.InstanceID}'");
    }

    return obj;
}
