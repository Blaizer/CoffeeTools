function arrayGetIndex(arg0, arg1)
{
    var l = array_length(arg0);
    
    for (var i = 0; i < l; i++)
    {
        if (arg0[i] == arg1)
            return i;
    }
    
    return -1;
}

function displayReset()
{
    if (!ct_is_paused())
    {
        if (global.STANDARD_FPS <= 60)
            display_reset(0, true);
        else
            display_reset(0, false);
    }
}

function modifyFps(arg0, arg1)
{
    if (!global.paused)
    {
        if (arg0)
        {
            if (global.STANDARD_FPS == 60)
                global.STANDARD_FPS = global.CT_TARGET_FPS;
            else
                global.STANDARD_FPS = 60;
        }
        
        if (arg1 != 0)
        {
            var list = [5, 10, 15, 20, 30, 40, 48, 60, 90, 120, 180, 240, 480, 960, 1920];
            var index = arrayGetIndex(list, global.STANDARD_FPS);
            
            if (index != -1 && (index + arg1) >= 0 && (index + arg1) < array_length(list))
            {
                global.STANDARD_FPS = list[index + arg1];
                global.CT_TARGET_FPS = global.STANDARD_FPS;
            }
        }
        
        displayReset();
        
        game_set_speed(global.STANDARD_FPS, gamespeed_fps);
    }
}

// Called exactly once per frame from CoffeeTools.dll at the very start of the frame, to increment any reliable counters.
function incrementFrame()
{
    if (!global.paused)
        global.FRAME_COUNT++;

    global.INPUT_FRAME++;
    global.CT_INPUTS_LENGTH = array_length(global.CT_INPUTS);
}

// Recursive function. Makes sure variable and any array entries are all able to be written to buffer.
function isVariableValid(arg0)
{
    if (is_array(arg0))
    {
        for (var i = 0; i < array_length(arg0); i++)
        {
            if (isVariableValid(arg0[i]) == false)
                return false;
        }
    }
    else if (is_real(arg0) || is_bool(arg0) || is_string(arg0) || ct_is_ref(arg0) || is_int64(arg0))
    {
    }
    else
    {
        return false;
    }
    
    return true;
}

// Writes variable type, and it's value to buffer.
function writeVariable(buffer, varValue)
{
    if (is_real(varValue))
        varType = 0;
    else if (is_bool(varValue))
        varType = 1;
    else if (is_string(varValue))
        varType = 2;
    else if (is_array(varValue))
        varType = 3;
    else if (ct_is_ref(varValue))
        varType = 4;
    else if (is_int64(varValue))
        varType = 5;
    else
        varType = 255;
    
    buffer_write(buffer, buffer_u8, varType);

    if (varType == 0)
    {
        buffer_write(buffer, buffer_f64, varValue);
    }
    else if (varType == 1)
    {
        buffer_write(buffer, buffer_u8, varValue);
    }
    else if (varType == 2)
    {
        buffer_write(buffer, buffer_string, varValue);
    }
    else if (varType == 3)
    {
        var writeArrayLength = array_length(varValue);
        buffer_write(buffer, buffer_u16, writeArrayLength);
        
        for (var i = 0; i < writeArrayLength; i++)
            writeVariable(buffer, varValue[i]);
    }
    else if (varType == 4)
    {
        buffer_write(buffer, buffer_u64, ct_ref_to_int64(varValue));
    }
    else if (varType == 5)
    {
        buffer_write(buffer, buffer_u64, varValue);
    }
    else
    {
        // Invalid variable, this shouldn't happen if you only call this function after checking isVariableValid first.
    }
}

function writeGlobalVariable(buffer, varName)
{
    var varValue = variable_global_get(varName);
    if (isVariableValid(varValue))
    {
        count++;
        buffer_write(buffer, buffer_string, varName);
        writeVariable(buffer, varValue);
    }
}

// Reads variable type, and value from buffer.
function readVariable(buffer)
{
    var readType = buffer_read(buffer, buffer_u8);
    var readValue;
    
    if (readType == 0)
    {
        readValue = buffer_read(buffer, buffer_f64);
    }
    else if (readType == 1)
    {
        readValue = buffer_read(buffer, buffer_u8) != 0 ? true : false;
    }
    else if (readType == 2)
    {
        readValue = buffer_read(buffer, buffer_string);
    }
    else if (readType == 3)
    {
        var readArrayLength = buffer_read(buffer, buffer_u16);
        readValue = array_create(readArrayLength);
        
        for (var i = 0; i < readArrayLength; i++)
            readValue[i] = readVariable(buffer);
    }
    else if (readType == 4)
    {
        readValue = ct_int64_to_ref(buffer_read(buffer, buffer_u64));
    }
    else if (readType == 5)
    {
        readValue = buffer_read(buffer, buffer_u64);
    }
    else if (readType == 255)
    {
        readValue = 0;
    }
    
    return readValue;
}

function readGlobalVariable(buffer)
{
    var varName = buffer_read(buffer, buffer_string);
    var varValue = readVariable(buffer);
    variable_global_set(varName, varValue);
}

function savestateSlot(arg0, arg1)
{
    if (!global.paused && global.currFile >= 1 && global.currFile <= global.NUM_PROFILES_ACCESSIBLE)
    {
        var name = "coffeetools/savestate" + string(global.currFile) + "-" + string(arg1);
        
        if (arg0)
        {
            surface_save(frameAdvanceSurface, name + ".png");

            appendFileBuffer = buffer_create(real(APPEND_FILE.HEADER_LENGTH), buffer_grow, 1);
            buffer_fill(appendFileBuffer, 0, buffer_u8, 0, real(APPEND_FILE.HEADER_LENGTH));
            buffer_write(appendFileBuffer, buffer_u32, APPEND_FILE.MAGIC);
            buffer_write(appendFileBuffer, buffer_u32, APPEND_FILE.VERSION);
            
            buffer_write(appendFileBuffer, buffer_u8, global.currGame);

            buffer_seek(appendFileBuffer, buffer_seek_start, real(APPEND_FILE.HEADER_LENGTH));

            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE GLOBALS");
            count = 0;
            countpoke = buffer_tell(appendFileBuffer);
            buffer_write(appendFileBuffer, buffer_u32, 0);
            variables = variable_instance_get_names(-5);
            //We only need to write global strings as game_save/game_load covers all other types of global variables in UFO50 v1.7.5
            for (var v = 0; v < array_length(variables); v++)
            {
                if(is_string(variable_global_get(variables[v])))
                {
                    writeGlobalVariable(appendFileBuffer, variables[v]);
                }
            }
            buffer_poke(appendFileBuffer, countpoke, buffer_u32, count);
            
            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE INSTANCE STRUCTS");
            if(instance_exists(o35_Game)) //Valbrace
            {
                buffer_write(appendFileBuffer, buffer_u8, 1);
                buffer_write(appendFileBuffer, buffer_string, json_stringify(o35_Game.floorSetpiece));
            }
            else if(instance_exists(o45__Game)) //Cyber Owls
            {
                buffer_write(appendFileBuffer, buffer_u8, 2);
                buffer_write(appendFileBuffer, buffer_string, json_stringify(o45__Game.missionStatus));
            }
            else
            {
                buffer_write(appendFileBuffer, buffer_u8, 0);
            }
            
            //Remember some camera info to hopefully fix rare 'zoomed in camera' glitch
            buffer_write(appendFileBuffer, buffer_u32, __view_get(e__VW.XView, 0));
            buffer_write(appendFileBuffer, buffer_u32, __view_get(e__VW.YView, 0));
            buffer_write(appendFileBuffer, buffer_u32, __view_get(e__VW.WView, 0));
            buffer_write(appendFileBuffer, buffer_u32, __view_get(e__VW.HView, 0));
            
            // For DataStructures, I was originally planning to use writeVariable() but instead I'm using GameMaker's serialisation functions (Example: ds_map_write())
            // This makes the filesize a little bit larger. In the future I might consider switching to writeVariable() if I'm sure it's bug-free.
            
            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE DS_MAPS");
            count = 0;
            countpoke = buffer_tell(appendFileBuffer);
            buffer_write(appendFileBuffer, buffer_u16, 0);
            for (var i = 0; i < real(C.DS_COUNT); i++)
            {
                if (ds_exists(i, ds_type_map))
                {
                    count++;
                    buffer_write(appendFileBuffer, buffer_u16, i);
                    buffer_write(appendFileBuffer, buffer_string, ds_map_write(i));
                }
            }
            buffer_poke(appendFileBuffer, countpoke, buffer_u16, count);

            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE DS_LISTS");
            count = 0;
            countpoke = buffer_tell(appendFileBuffer);
            buffer_write(appendFileBuffer, buffer_u16, 0);
            for (var i = 0; i < real(C.DS_COUNT); i++)
            {
                if (ds_exists(i, ds_type_list))
                {
                    count++;
                    buffer_write(appendFileBuffer, buffer_u16, i);
                    buffer_write(appendFileBuffer, buffer_string, ds_list_write(i));
                }
            }
            buffer_poke(appendFileBuffer, countpoke, buffer_u16, count);
            
            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE DS_STACKS");
            count = 0;
            countpoke = buffer_tell(appendFileBuffer);
            buffer_write(appendFileBuffer, buffer_u16, 0);
            for (var i = 0; i < real(C.DS_COUNT); i++)
            {
                if (ds_exists(i, ds_type_stack))
                {
                    count++;
                    buffer_write(appendFileBuffer, buffer_u16, i);
                    buffer_write(appendFileBuffer, buffer_string, ds_stack_write(i));
                }
            }
            buffer_poke(appendFileBuffer, countpoke, buffer_u16, count);

            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE DS_QUEUES");
            count = 0;
            countpoke = buffer_tell(appendFileBuffer);
            buffer_write(appendFileBuffer, buffer_u16, 0);
            for (var i = 0; i < real(C.DS_COUNT); i++)
            {
                if (ds_exists(i, ds_type_queue))
                {
                    count++;
                    buffer_write(appendFileBuffer, buffer_u16, i);
                    buffer_write(appendFileBuffer, buffer_string, ds_queue_write(i));
                }
            }
            buffer_poke(appendFileBuffer, countpoke, buffer_u16, count);

            if (LOG.LEVEL >= LOG.VERBOSE) trace("WRITE DS_GRIDS");
            count = 0;
            countpoke = buffer_tell(appendFileBuffer);
            buffer_write(appendFileBuffer, buffer_u16, 0);
            for (var i = 0; i < real(C.DS_COUNT); i++)
            {
                if (ds_exists(i, ds_type_grid))
                {
                    count++;
                    buffer_write(appendFileBuffer, buffer_u16, i);
                    buffer_write(appendFileBuffer, buffer_u16, ds_grid_width(i));
                    buffer_write(appendFileBuffer, buffer_u16, ds_grid_height(i));
                    buffer_write(appendFileBuffer, buffer_string, ds_grid_write(i));
                }
            }
            buffer_poke(appendFileBuffer, countpoke, buffer_u16, count);
            
            if (LOG.LEVEL >= LOG.VERBOSE) trace("Finished");
            writeTasFile(name + ".ctas");
            game_save(name + ".dat");
            
            //Reread the append file buffer to fix the global strings issue in UFO50 v1.7.5
            buffer_seek(appendFileBuffer, buffer_seek_start, real(APPEND_FILE.HEADER_LENGTH));
            count = buffer_read(appendFileBuffer, buffer_u32);
            for (var i = 0; i < count; i++)
            {
                readGlobalVariable(appendFileBuffer);
            }

            buffer_save(appendFileBuffer, name + "a.dat");
            buffer_delete(appendFileBuffer);
            
            global.CT_MsgText = "SLOT " + string(arg1) + " SAVED";
            global.CT_MessageTimer = current_time + 1000;
        }
        else if (file_exists(name + ".dat"))
        {
            if (file_exists(name + "a.dat"))
            {
                //Check first if savestate being loaded is in the same game as current.
                appendFileBuffer = 0;
                appendFileBuffer = buffer_load(name + "a.dat");
                var magic = buffer_read(appendFileBuffer, buffer_u32);
                var version = buffer_read(appendFileBuffer, buffer_u32);
                var loadingSavestateGameNumber = buffer_read(appendFileBuffer, buffer_u8);
                buffer_seek(appendFileBuffer, buffer_seek_start, real(APPEND_FILE.HEADER_LENGTH));

                if (magic != APPEND_FILE.MAGIC || version != APPEND_FILE.VERSION)
                {
                    global.CT_MsgText = "SLOT " + string(arg1) + " BLOCKED (INVALID savestate"+string(global.currFile) + "-" + string(arg1)+"a.dat)";
                    global.CT_MessageTimer = current_time + 1500;
                }
                else if(loadingSavestateGameNumber != global.currGame)
                {
                    if(loadingSavestateGameNumber == 0)
                        global.CT_MsgText = "SLOT " + string(arg1) + " BLOCKED (LIBRARY)";
                    else
                        global.CT_MsgText = "SLOT " + string(arg1) + " BLOCKED ("+string(global.mGameTitle[loadingSavestateGameNumber])+")";
                    global.CT_MessageTimer = current_time + 1500;
                }
                else
                {
                    if (ct_is_paused() && file_exists(name + ".png"))
                    {
                        sprite = sprite_add(name + ".png", 1, 0, 0, 0, 0);
                        surface_set_target(frameAdvanceSurface);
                        draw_sprite(sprite, 0, 0, 0);
                        surface_reset_target();
                        sprite_delete(sprite);
                    }
                    
                    //Write CoffeeTools-related and other unchanging variables to temp buffer
                    count = 0;
                    var tempMemoryBuffer = buffer_create(0, buffer_grow, 1);
                    buffer_write(tempMemoryBuffer, buffer_u32, 0);
                    variables = variable_instance_get_names(-5);
                    for (var i = 0; i < array_length(variables); i++)
                    {
                        var n = variables[i];
                        if (string_copy(n, 0, 3) == "CT_" || n == "STANDARD_FPS" || n == "BGM" || string_copy(n, 1, 3) == "SFX")
                        {
                            writeGlobalVariable(tempMemoryBuffer, n);
                        }
                    }
                    buffer_poke(tempMemoryBuffer, 0, buffer_u32, count);
                    //Quick fix for Library music
                    buffer_write(tempMemoryBuffer, buffer_u32, oAudioHandler.bgmLibraryNormal);
                    buffer_write(tempMemoryBuffer, buffer_u32, oAudioHandler.bgmLibraryGarden);
                    buffer_write(tempMemoryBuffer, buffer_u32, oAudioHandler.bgmLibraryInfinity);
                    
                    ct_game_load(name + ".dat");
                    
                    count = buffer_read(appendFileBuffer, buffer_u32);
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ GLOBALS");
                    for (var i = 0; i < count; i++)
                    {
                        readGlobalVariable(appendFileBuffer);
                    }
                    
                    count = buffer_read(appendFileBuffer, buffer_u8); //not actually count. (0 = None, 1 = Valbrace, 2 = Cyber Owls)
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ INSTANCE STRUCTS");
                    if(count == 1)
                    {
                        o35_Game.floorSetpiece = json_parse(buffer_read(appendFileBuffer, buffer_string));
                    }
                    else if(count == 2)
                    {
                        //json_stringify() didn't include method variables, so missionStatus will be fixed later in the script
                        o45__Game.missionStatus = json_parse(buffer_read(appendFileBuffer, buffer_string));
                    }
                    
                    //Remember some camera info to hopefully fix rare 'zoomed in camera' glitch
                    __view_set(e__VW.XView, 0, buffer_read(appendFileBuffer, buffer_u32));
                    __view_set(e__VW.YView, 0, buffer_read(appendFileBuffer, buffer_u32));
                    __view_set(e__VW.WView, 0, buffer_read(appendFileBuffer, buffer_u32));
                    __view_set(e__VW.HView, 0, buffer_read(appendFileBuffer, buffer_u32));
                    
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ DS_MAPS");
                    count = buffer_read(appendFileBuffer, buffer_u16);
                    fillerDSArray = [];
                    
                    for (var i = 0; i < real(C.DS_COUNT); i++)
                    {
                        if (ds_exists(i, ds_type_map))
                            ds_map_destroy(i);
                    }
                    
                    for (var i = 0; i < count; i++)
                    {
                        loadDSID = buffer_read(appendFileBuffer, buffer_u16);
                        loadDS = ds_map_create();
                        f = 0;
    
                        // If there's a gap in IDs (example 1,2,3,5), we create 'filler' data structures until we can give the loaded data structure it's correct ID.
                        // 'Filler' data structures are deleted afterwards.
                        // This can be reproduced by entering Vainger and then exiting to library.
                        while (loadDS != loadDSID)
                        {
                            array_push(fillerDSArray, loadDS);
                            loadDS = ds_map_create();
                            f++;
                            
                            if (f == 200)
                            {
                                show_message("FAILSAFE: DS_MAP filler. Wanted ID:" + string(loadDSID) + " Got ID:" + string(loadDS));
                                i = count;
                                break;
                            }
                        }
                        
                        ds_map_read(loadDS, buffer_read(appendFileBuffer, buffer_string));
                    }
                    
                    for (var i = 0; i < array_length(fillerDSArray); i++)
                        ds_map_destroy(fillerDSArray[i]);
                    
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ DS_LISTS");
                    count = buffer_read(appendFileBuffer, buffer_u16);
                    fillerDSArray = [];
                    
                    for (var i = 0; i < real(C.DS_COUNT); i++)
                    {
                        if (ds_exists(i, ds_type_list))
                            ds_list_destroy(i);
                    }
                    
                    for (var i = 0; i < count; i++)
                    {
                        loadDSID = buffer_read(appendFileBuffer, buffer_u16);
                        loadDS = ds_list_create();
                        f = 0;
                        
                        while (loadDS != loadDSID)
                        {
                            array_push(fillerDSArray, loadDS);
                            loadDS = ds_list_create();
                            f++;
                            
                            if (f == 200)
                            {
                                show_message("FAILSAFE: DS_LIST filler. Wanted ID:" + string(loadDSID) + " Got ID:" + string(loadDS));
                                i = count;
                                break;
                            }
                        }
                        
                        ds_list_read(loadDS, buffer_read(appendFileBuffer, buffer_string));
                    }
                    
                    for (var i = 0; i < array_length(fillerDSArray); i++)
                        ds_list_destroy(fillerDSArray[i]);
                    
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ DS_STACKS");
                    count = buffer_read(appendFileBuffer, buffer_u16);
                    fillerDSArray = [];
                    
                    for (var i = 0; i < real(C.DS_COUNT); i++)
                    {
                        if (ds_exists(i, ds_type_stack))
                            ds_stack_destroy(i);
                    }
                    
                    for (var i = 0; i < count; i++)
                    {
                        loadDSID = buffer_read(appendFileBuffer, buffer_u16);
                        loadDS = ds_stack_create();
                        f = 0;
                        
                        while (loadDS != loadDSID)
                        {
                            array_push(fillerDSArray, loadDS);
                            loadDS = ds_stack_create();
                            f++;
                            
                            if (f == 200)
                            {
                                show_message("FAILSAFE: DS_STACK filler. Wanted ID:" + string(loadDSID) + " Got ID:" + string(loadDS));
                                i = count;
                                break;
                            }
                        }
                        
                        ds_stack_read(loadDS, buffer_read(appendFileBuffer, buffer_string));
                    }
                    
                    for (var i = 0; i < array_length(fillerDSArray); i++)
                        ds_stack_destroy(fillerDSArray[i]);
                    
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ DS_QUEUES");
                    count = buffer_read(appendFileBuffer, buffer_u16);
                    fillerDSArray = [];
                    
                    for (var i = 0; i < real(C.DS_COUNT); i++)
                    {
                        if (ds_exists(i, ds_type_queue))
                            ds_queue_destroy(i);
                    }
                    
                    for (var i = 0; i < count; i++)
                    {
                        loadDSID = buffer_read(appendFileBuffer, buffer_u16);
                        loadDS = ds_queue_create();
                        f = 0;
                        
                        while (loadDS != loadDSID)
                        {
                            array_push(fillerDSArray, loadDS);
                            loadDS = ds_queue_create();
                            f++;
                            
                            if (f == 200)
                            {
                                show_message("FAILSAFE: DS_QUEUE filler. Wanted ID:" + string(loadDSID) + " Got ID:" + string(loadDS));
                                i = count;
                                break;
                            }
                        }
                        
                        ds_queue_read(loadDS, buffer_read(appendFileBuffer, buffer_string));
                    }
                    
                    for (var i = 0; i < array_length(fillerDSArray); i++)
                        ds_queue_destroy(fillerDSArray[i]);
                    
                    if (LOG.LEVEL >= LOG.VERBOSE) trace("READ DS_GRIDS");
                    count = buffer_read(appendFileBuffer, buffer_u16);
                    fillerDSArray = [];
                    
                    for (var i = 0; i < real(C.DS_COUNT); i++)
                    {
                        if (ds_exists(i, ds_type_grid))
                            ds_grid_destroy(i);
                    }
                    
                    for (var i = 0; i < count; i++)
                    {
                        loadDSID = buffer_read(appendFileBuffer, buffer_u16);
                        loadDSWidth = buffer_read(appendFileBuffer, buffer_u16);
                        loadDSHeight = buffer_read(appendFileBuffer, buffer_u16);
                        loadDS = ds_grid_create(loadDSWidth, loadDSHeight);
                        f = 0;
                        
                        while (loadDS != loadDSID)
                        {
                            array_push(fillerDSArray, loadDS);
                            loadDS = ds_grid_create(loadDSWidth, loadDSHeight);
                            f++;
                            
                            if (f == 200)
                            {
                                show_message("FAILSAFE: DS_GRID filler. Wanted ID:" + string(loadDSID) + " Got ID:" + string(loadDS));
                                i = count;
                                break;
                            }
                        }
                        
                        ds_grid_read(loadDS, buffer_read(appendFileBuffer, buffer_string));
                    }
                    
                    for (var i = 0; i < array_length(fillerDSArray); i++)
                        ds_grid_destroy(fillerDSArray[i]);
                    
                    //Reload text structs from file
                    scrLoadLibraryText();
                    scrLoadGameText();
                    
                    //Quick fix to Vainger room graphics
                    if (room == rm07_GravGuns || room == rm07_GravGuns2 || room == rm07_GravGuns3 || room == rm07_GravGuns4)
                    {
                        var elems = layer_get_all_elements(layer_get_id("PrerenderedRoomWalls"));
                        
                        for (var v = 0; v < array_length(elems); v++)
                        {
                            if (layer_get_element_type(elems[v]) == 4)
                                layer_sprite_index(elems[v], floor(layer_sprite_get_x(elems[v]) / 384) + (floor(layer_sprite_get_y(elems[v]) / 216) * 10));
                        }
                        
                        elems = layer_get_all_elements(layer_get_id("PrerenderedRoomShadows"));
                        
                        for (var v = 0; v < array_length(elems); v++)
                        {
                            if (layer_get_element_type(elems[v]) == 4)
                                layer_sprite_index(elems[v], floor(layer_sprite_get_x(elems[v]) / 384) + (floor(layer_sprite_get_y(elems[v]) / 216) * 10));
                        }
                    }
                    
                    //Manually fix method variables because otherwise the games will crash
                    with(oTitleScreens)
                    {
                        dataIsLoaded = function()
                        {
                            if (global.currGameID != 40 && !audio_group_is_loaded(global.AUDIOGROUP_BGM[global.currGameID]))
                                return false;
                            
                            if (texturegroup_get_status(global.TEXTUREGROUP[global.currGameID]) != 3)
                                return false;
                            
                            return true;
                        };
                    }
                    with (o09__Player) //Fist Hell player(s)
                    {
                        zeroSpeed = function()
                        {
                            hspd = 0;
                            vspd = 0;
                        };
                        
                        isMoving = function()
                        {
                            return hspd != 0 || vspd != 0;
                        };
                        
                        endCharge = function()
                        {
                            chargeCount = -1;
                            throwCharge = -1;
                            pileCharge = -1;
                            chargeBlink = false;
                            alarm[2] = -1;
                        };
                        
                        animFree = function()
                        {
                            if (animState != 12 && animState != 15 && animState != 13 && animState != 14 && animState != 16 && animState != 9 && animState != 4 && animState != 5 && animState != 6 && animState != 10 && animState != 22 && animState != 21 && animState != 25)
                                return true;
                            else
                                return false;
                        };
                    }
                    with (o10__Item) //Porgy items
                    {
                        set_depth = function()
                        {
                            if (!place_meeting(x, y, obj_floor))
                                depth = 500;
                            else
                                depth = 1500;
                        };
                    }
                    with (o31_Mas) //Elfazar's Hat master
                    {
                        update_depth = function(arg0)
                        {
                            with (o31__Destructible)
                                depth = -(y - arg0) + 50;
                        };
                    }
                    with (o35_iSpellWall) //Valbrace spell wall
                    {
                        scrGetRandomState = function()
                        {
                            return [random_get_seed(), global.rng_state_1, global.rng_state_2];
                        };
                    }
                    with (o38_Mas) //Campanella 2 master
                    {
                        isVoid = function(arg0)
                        {
                            if (arg0 > TILE_VOID_FILL)
                                return false;
                            
                            return true;
                        };
                        
                        isCorridor = function(arg0)
                        {
                            if (arg0 < TILE_CORRIDOR_HORI)
                                return false;
                            
                            if (arg0 > TILE_CORRIDOR_DEAD_END)
                                return false;
                            
                            return true;
                        };
                        
                        isOpen = function(arg0)
                        {
                            if (arg0 < TILE_OPEN_FREE)
                                return false;
                            
                            return true;
                        };
                    }
                    with(o45__Game) //Cyber Owls master's missionStatus struct
                    {
                        missionStatus.get_done = function(arg0)
                        {
                            return struct_get(self, arg0).won && !struct_get(self, arg0).captured;
                        };
                        
                        missionStatus.get_finished_count = function()
                        {
                            return get_done("octavio") + get_done("engle") + get_done("guin") + get_done("huxley");
                        };
                        
                        missionStatus.get_lowest_finished = function()
                        {
                            if (!get_done("octavio"))
                                return 0;
                            
                            if (!get_done("engle"))
                                return 1;
                            
                            if (!get_done("guin"))
                                return 2;
                            
                            if (!get_done("huxley"))
                                return 3;
                            
                            return 4;
                        };
                    }
                    with(o46_Mas) //Caramel Caramel master
                    {
                        enemy_control = function(arg0)
                        {
                            with (o46__EnemyPar)
                            {
                                if (!autoControl)
                                    continue;
                                
                                if (!popin)
                                {
                                    if (x <= (arg0 + 384))
                                        popin = true;
                                }
                                
                                if (state == -1)
                                {
                                    if (x < (arg0 + 384 + 32))
                                    {
                                        speed = mSpeed;
                                        state = 0;
                                    }
                                }
                                else if (x < (arg0 - 96) || y < -32)
                                {
                                    instance_destroy();
                                }
                            }
                        };
                    }
                    with(o48_Hazard) //Planet Zoldath hazards
                    {
                        destroy_if_overlapping = function()
                        {
                            if (instance_place(x, y - 16, oJTopDown_Wall))
                                instance_destroy();
                            else if (instance_place(x, y - 32, o48_Entrance))
                                instance_destroy();
                        };
                        
                        get_exploded = function()
                        {
                            if (portal)
                                exit;
                            
                            if (noPortalTimer)
                                exit;
                            
                            with (o48_Hazard)
                            {
                                image_index = 0;
                                image_speed = 0;
                                portal = false;
                            }
                            
                            portal = true;
                            image_speed = 0.1;
                            var loops = 0;
                            
                            do
                            {
                                linkedPortal = instance_find(o48_Hazard, scrIRandomRange(0, instance_number(o48_Hazard) - 1));
                                loops++;
                                
                                if (loops >= 10)
                                    portal = false;
                            }
                            until (linkedPortal != id && (linkedPortal.xPos != xPos || linkedPortal.yPos != yPos));
                            
                            linkedPortal.portal = true;
                            linkedPortal.linkedPortal = id;
                            linkedPortal.image_speed = 0.1;
                        };
                    }
                    with (o48_Hole) //Planet Zoldath holes
                    {
                        initialize = function()
                        {
                            if (instance_place(x, y - 16, o48__Obstacle) && !instance_place(x, y - 16, o48_UFO))
                            {
                                instance_destroy();
                                exit;
                            }
                            
                            if (!instance_place(x, y + 16, o48_Hole) && !instance_place(x, y + 16, o48_HoleNotObstacle))
                            {
                                instance_create(x, y, o48_HoleNotObstacle);
                                instance_destroy();
                                exit;
                            }
                            
                            if (!instance_place(x, y - 16, o48_Hole))
                                image_index = 0;
                        };
                    }
                    with (o48_Tree) //Planet Zoldath trees
                    {
                        get_exploded = function()
                        {
                            scrFX(s48_EnemyDie, 0.3, x + 16, y + 24);
                            instance_destroy();
                        };
                    }
                    with (o48_TreeSmall) //Planet Zoldath small trees
                    {
                        get_exploded = function()
                        {
                            scrFX(s48_EnemyDie, 0.3, x + 8, y + 16);
                            instance_destroy();
                        };
                    }
                    //Read CoffeeTools preferences and other unchanging variables from temp buffer
                    buffer_seek(tempMemoryBuffer, buffer_seek_start, 0);
                    count = buffer_read(tempMemoryBuffer, buffer_u32);
                    var sfxPrev = global.SFX;
                    for (var i = 0; i < count; i++)
                    {
                        readGlobalVariable(tempMemoryBuffer);
                    }
                    //If a looping SFX is still playing when it shouldn't be, then stop it
                    if (global.SFX_LOOP && global.SFX != sfxPrev)
                        mute(global.SFX);
                    //Quick fix for Library music
                    oAudioHandler.bgmLibraryNormal = buffer_read(tempMemoryBuffer, buffer_u32);
                    oAudioHandler.bgmLibraryGarden = buffer_read(tempMemoryBuffer, buffer_u32);
                    oAudioHandler.bgmLibraryInfinity = buffer_read(tempMemoryBuffer, buffer_u32);
                    //Quick fix in case user is loading a savestate during a freeze frame
                    game_set_speed(global.STANDARD_FPS, gamespeed_fps);
                    //On the frame a savestate was just loaded, we will fix any inputs that are incongruent with the previous frame.
                    global.CT_FixIncongruentInputsFrame = true;
                    buffer_delete(tempMemoryBuffer);
                    
                    readTasFile(name + ".ctas");
                    
                    global.CT_MsgText = "SLOT " + string(arg1) + " LOADED";
                    global.CT_MessageTimer = current_time + 1000;
                }
            }
            else
            {
                global.CT_MsgText = "SLOT " + string(arg1) + " BLOCKED (MISSING savestate"+string(global.currFile) + "-" + string(arg1)+"a.dat)";
                global.CT_MessageTimer = current_time + 1500;
            }
            buffer_delete(appendFileBuffer);
        }
    }
}

function trackPresses(arg0, arg1, arg2)
{
    if (multiPressedState[arg0][arg1] == 0)
    {
        if (arg2)
            multiPressed[arg0][arg1] += 1;
        else
            multiPressed[arg0][arg1] = 0;
        
        if (multiPressed[arg0][arg1] > 1)
            multiPressedState[arg0][arg1] = 1;
    }
    else if (multiPressedState[arg0][arg1] == 1)
    {
        if (arg2)
        {
            multiPressed[arg0][arg1] += 1;
            
            if (multiPressed[arg0][arg1] == 4)
                multiPressedState[arg0][arg1] = -30;
        }
        else
        {
            multiPressedState[arg0][arg1] = -30;
        }
    }
    else if (multiPressedState[arg0][arg1] < 0)
    {
        multiPressedState[arg0][arg1] += 1;
        
        if (multiPressedState[arg0][arg1] == 0)
            multiPressed[arg0][arg1] = 0;
    }
}

function drawKey(arg0, arg1, arg2, arg3, arg4, arg5)
{
    if (arg3)
        draw_sprite_ext(sVirtualInputs, arg0, arg1, arg2, 1, 1, 0, global.palette[0], 1);
    else
        draw_sprite_ext(sVirtualInputs, arg0, arg1, arg2, 1, 1, 0, global.palette[16], 1);
    
    if (arg4)
        draw_sprite(sVirtualInputs2, arg0, arg1, arg2);
    
    if (arg5)
        draw_sprite(sVirtualInputs2, arg0 + 8, arg1, arg2);
}

function keyboardCheckRepeat(vk)
{
    if (ct_keyboard_check_pressed(vk))
    {
        global.CT_KEY_AUTO_REPEAT[vk] = current_time + real(C.AUTO_REPEAT_INITIAL_DELAY);
        return true;
    }
    else if (ct_keyboard_check(vk))
    {
        if (current_time > global.CT_KEY_AUTO_REPEAT[vk])
        {
            global.CT_KEY_AUTO_REPEAT[vk] = max(current_time, global.CT_KEY_AUTO_REPEAT[vk] + real(C.AUTO_REPEAT_DELAY));
            return true;
        }
    }
    else
    {
        global.CT_KEY_AUTO_REPEAT[vk] = 0;
    }

    return false;
}

// Called from CoffeeTools.dll every frame, and also while paused, to get input.
// the ct_keyboard_* functions should be used from this function as the regular keyboard_* functions won't get updated while paused.
function performActions()
{
    var wasPaused = ct_is_paused();

    if (ct_keyboard_check_pressed(vk_f2))
    {
        global.CT_DisplayP2Inputs = !global.CT_DisplayP2Inputs;
        global.CT_DisplayInputs = true;
    }

    if (ct_keyboard_check_pressed(vk_f3))
    {
        global.CT_DisplayInputs = !global.CT_DisplayInputs;
    }

    if (ct_keyboard_check_pressed(vk_f4))
    {
        global.CT_BlockSaves = !global.CT_BlockSaves;
    }

    if (ct_keyboard_check_pressed(vk_f5))
    {
        global.CT_FeedbackMessages = !global.CT_FeedbackMessages;
        global.CT_MessageTimer2 = current_time + 500;
    }

    if (ct_keyboard_check_pressed(vk_f6))
    {
        global.CT_ShowFrameCounter = !global.CT_ShowFrameCounter;
    }

    if (ct_keyboard_check_pressed(vk_f7))
    {
        global.FRAME_COUNT = 0;
        global.CT_ShowFrameCounter = true;
    }

    if (ct_keyboard_check_pressed(vk_f8))
    {
        global.CT_ShowTasFrameCounter = !global.CT_ShowTasFrameCounter;
    }

    var resetSpeed = false;
    var changeSpeed = 0;

    if (keyboardCheckRepeat(vk_space))
    {
        if (ct_keyboard_check(vk_control))
        {
            ct_set_paused(!ct_is_paused());
        }
        else if (ct_is_paused())
        {
            ct_set_paused(true, true);
        }
        else
        {
            ct_set_paused(true);
        }
    }

    if (ct_keyboard_check_pressed(vk_backspace))
    {
        if (ct_is_paused())
        {
            ct_set_paused(false);
        }
        else
        {
            resetSpeed = true;
        }
    }

    if (keyboardCheckRepeat(189))
    {
        changeSpeed--;
    }

    if (keyboardCheckRepeat(187))
    {
        changeSpeed++;
    }

    if (resetSpeed || changeSpeed != 0)
    {
        modifyFps(resetSpeed, changeSpeed);
    }

    var isSaving = false;
    var slot = 0;

    for (var i = 0; i < 10; i++)
    {
        if (ct_keyboard_check_pressed(i + ord("0")))
        {
            slot = i == 0 ? 10 : i;
            isSaving = ct_keyboard_check(vk_shift);
        }
    }
    
    if (slot != 0)
    {
        savestateSlot(isSaving, slot);
    }

    if (!ct_is_paused() && wasPaused)
    {
        displayReset();
    }
}

// Called from CoffeeTools.dll when the screen needs to be refreshed while we're paused (at roughly 60FPS).
function refreshScreen()
{
    surface_set_target(application_surface);
    draw_surface(frameAdvanceSurface, 0, 0);
    surface_reset_target();
    drawScreenWithToolsText();
    //event_perform(ev_draw, ev_gui_end);
}

function drawToolsText()
{
    surface_set_target(application_surface);

    if (DEBUG.MODE == DEBUG.ON)
    {
        draw_set_font(global.fontThinOutline);
        
        debugCursorX = window_mouse_get_x() / global.scale;
        debugCursorY = window_mouse_get_y() / global.scale;
        
        // debugScreen = 0 : Hidden
        // debugScreen = 1 : Showing list of data structures
        // debugScreen = 2 : Viewing a specific data structure
        
        if (keyboard_check_pressed(vk_control))
        {
            if (debugScreen == 1)
                debugScreen = 0;
            else if (debugScreen == 0 || debugScreen == 2)
                debugScreen = 1;
        }
        
        if (debugScreen == 1)
        {
            draw_set_color(c_aqua);
            draw_set_alpha(0.5);
            draw_line(0, 0, 0, 216);
            draw_line(64, 0, 64, 216);
            draw_line(128, 0, 128, 216);
            draw_line(192, 0, 192, 216);
            draw_line(256, 0, 256, 216);
            draw_set_alpha(1);
            draw_text(0, 0, "MAP");
            draw_text(64, 0, "LIST");
            draw_text(128, 0, "STACK");
            draw_text(192, 0, "QUEUE");
            draw_text(256, 0, "GRID");
            draw_set_color(c_white);
            debugYOffset = 8;
            
            for (var i = 0; i < 255; i++)
            {
                if (ds_exists(i, ds_type_map))
                {
                    if (debugCursorX >= 0 && debugCursorX < 64 && debugCursorY >= debugYOffset && debugCursorY < (debugYOffset + 8))
                    {
                        draw_set_alpha(0.5);
                        draw_rectangle(0, debugYOffset, 63, debugYOffset + 7, false);
                        draw_set_alpha(1);
                        
                        if (mouse_check_button_pressed(mb_left))
                        {
                            debugScreen = 2;
                            debugDSID = i;
                            debugDSType = 1;
                            debugDSOffsetY = 0;
                        }
                    }
                    
                    draw_set_color(c_yellow);
                    draw_text(0, debugYOffset, i);
                    draw_set_color(c_white);
                    draw_text(24, debugYOffset, ds_map_size(i));
                    debugYOffset += 8;
                }
            }
            
            draw_text(32, 0, (debugYOffset - 8) / 8);
            debugYOffset = 8;
            
            for (var i = 0; i < 255; i++)
            {
                if (ds_exists(i, ds_type_list))
                {
                    if (debugCursorX >= 64 && debugCursorX < 128 && debugCursorY >= debugYOffset && debugCursorY < (debugYOffset + 8))
                    {
                        draw_set_alpha(0.5);
                        draw_rectangle(64, debugYOffset, 127, debugYOffset + 7, false);
                        draw_set_alpha(1);
                        
                        if (mouse_check_button_pressed(mb_left))
                        {
                            debugScreen = 2;
                            debugDSID = i;
                            debugDSType = 2;
                            debugDSOffsetY = 0;
                        }
                    }
                    
                    draw_set_color(c_yellow);
                    draw_text(64, debugYOffset, i);
                    draw_set_color(c_white);
                    draw_text(88, debugYOffset, ds_list_size(i));
                    debugYOffset += 8;
                    
                    if (mouse_check_button_pressed(mb_right))
                        ds_list_destroy(i);
                }
            }
            
            draw_text(96, 0, (debugYOffset - 8) / 8);
            debugYOffset = 8;
            
            for (var i = 0; i < 255; i++)
            {
                if (ds_exists(i, ds_type_stack))
                {
                    if (debugCursorX >= 128 && debugCursorX < 192 && debugCursorY >= debugYOffset && debugCursorY < (debugYOffset + 8))
                    {
                        draw_set_alpha(0.5);
                        draw_rectangle(128, debugYOffset, 191, debugYOffset + 7, false);
                        draw_set_alpha(1);
                        
                        if (mouse_check_button_pressed(mb_left))
                        {
                            debugScreen = 2;
                            debugDSID = i;
                            debugDSType = 3;
                            debugDSStackQueueRead = ds_stack_write(debugDSID);
                        }
                    }
                    
                    draw_set_color(c_yellow);
                    draw_text(128, debugYOffset, i);
                    draw_set_color(c_white);
                    draw_text(152, debugYOffset, ds_stack_size(i));
                    debugYOffset += 8;
                }
            }
            
            draw_text(160, 0, (debugYOffset - 8) / 8);
            debugYOffset = 8;
            
            for (var i = 0; i < 255; i++)
            {
                if (ds_exists(i, ds_type_queue))
                {
                    if (debugCursorX >= 192 && debugCursorX < 256 && debugCursorY >= debugYOffset && debugCursorY < (debugYOffset + 8))
                    {
                        draw_set_alpha(0.5);
                        draw_rectangle(192, debugYOffset, 255, debugYOffset + 7, false);
                        draw_set_alpha(1);
                        
                        if (mouse_check_button_pressed(mb_left))
                        {
                            debugScreen = 2;
                            debugDSID = i;
                            debugDSType = 4;
                            debugDSStackQueueRead = ds_queue_write(debugDSID);
                        }
                    }
                    
                    draw_set_color(c_yellow);
                    draw_text(192, debugYOffset, i);
                    draw_set_color(c_white);
                    draw_text(216, debugYOffset, ds_queue_size(i));
                    debugYOffset += 8;
                }
            }
            
            draw_text(224, 0, (debugYOffset - 8) / 8);
            debugYOffset = 8;
            
            for (var i = 0; i < 255; i++)
            {
                if (ds_exists(i, ds_type_grid))
                {
                    if (debugCursorX >= 256 && debugCursorX < 328 && debugCursorY >= debugYOffset && debugCursorY < (debugYOffset + 8))
                    {
                        draw_set_alpha(0.5);
                        draw_rectangle(256, debugYOffset, 327, debugYOffset + 7, false);
                        draw_set_alpha(1);
                        
                        if (mouse_check_button_pressed(mb_left))
                        {
                            debugScreen = 2;
                            debugDSID = i;
                            debugDSType = 5;
                            debugDSOffsetX = 0;
                            debugDSOffsetY = 0;
                            debugDSOffsetWidth = 32;
                        }
                    }
                    
                    draw_set_color(c_yellow);
                    draw_text(256, debugYOffset, i);
                    draw_set_color(c_white);
                    draw_text(280, debugYOffset, ds_grid_width(i));
                    draw_text(304, debugYOffset, ds_grid_height(i));
                    debugYOffset += 8;
                }
            }
            
            draw_text(288, 0, (debugYOffset - 8) / 8);
        }
        else if (debugScreen == 2)
        {
            if (debugScreen == 2)
            {
                debugDSOffsetX += (8 * (keyboard_check(vk_numpad1) - keyboard_check(vk_numpad3)));
                debugDSOffsetY += (8 * (keyboard_check(vk_numpad5) - keyboard_check(vk_numpad2)));
                debugDSOffsetWidth += (12 * (keyboard_check_pressed(vk_numpad6) - keyboard_check_pressed(vk_numpad4)));
                debugDSOffsetWidth = clamp(debugDSOffsetWidth, 20, 104);
            }
            else
            {
                debugDSID += (keyboard_check_pressed(vk_numpad2) - keyboard_check_pressed(vk_numpad8));
                debugDSID = clamp(debugDSID, 0, 255);
                debugDSType += (keyboard_check_pressed(vk_numpad6) - keyboard_check_pressed(vk_numpad4));
                debugDSType = clamp(debugDSType, 1, 5);
            }
            
            if (keyboard_check_pressed(vk_numpad6) - keyboard_check_pressed(vk_numpad4))
                debugDSID = 0;
            
            draw_set_color(c_black);
            draw_set_alpha(0.5);
            draw_rectangle(0, 0, 383, 215, false);
            draw_set_alpha(1);
            draw_set_color(c_aqua);
            draw_text(48, 0, "ID:" + string(debugDSID));
            
            if (debugDSType == 1)
            {
                draw_text(16, 0, "MAP");
                
                if (ds_exists(debugDSID, debugDSType))
                {
                    draw_text(80, 0, "SIZE:" + string(ds_map_size(debugDSID)));
                    debugDSMapKey = ds_map_find_first(debugDSID);
                    
                    for (var yy = 0; yy < ds_map_size(debugDSID); yy++)
                    {
                        draw_set_color(c_yellow);
                        draw_text(0, debugDSOffsetY + 8 + (yy * 8), debugDSMapKey);
                        draw_set_color(c_white);
                        draw_text(112, debugDSOffsetY + 8 + (yy * 8), ds_map_find_value(debugDSID, debugDSMapKey));
                        debugDSMapKey = ds_map_find_next(debugDSID, debugDSMapKey);
                    }
                }
            }
            
            if (debugDSType == 2)
            {
                draw_text(16, 0, "LIST");
                
                if (ds_exists(debugDSID, debugDSType))
                {
                    draw_text(80, 0, "SIZE:" + string(ds_list_size(debugDSID)));
                    draw_set_color(c_white);
                    
                    for (var yy = 0; yy < ds_list_size(debugDSID); yy++)
                    {
                        draw_set_color(c_yellow);
                        draw_text(0, debugDSOffsetY + 8 + (yy * 8), yy);
                        draw_set_color(c_white);
                        draw_text(24, debugDSOffsetY + 8 + (yy * 8), ds_list_find_value(debugDSID, yy));
                    }
                }
            }
            else if (debugDSType == 3)
            {
                draw_text(16, 0, "STACK");
                
                if (ds_exists(debugDSID, debugDSType))
                {
                    draw_text(80, 0, "SIZE:" + string(ds_stack_size(debugDSID)));
                    draw_set_color(c_white);
                    
                    if (keyboard_check_pressed(vk_numpad7) || keyboard_check(vk_numpad8))
                        debugDSStackQueueRead = ds_stack_write(debugDSID);
                    
                    draw_set_color(c_yellow);
                    draw_text(0, debugDSOffsetY + 8, "(Press NUMPAD7 or Hold NUMPAD8 to update. May cause lag.");
                    draw_set_color(c_white);
                    var xx = 0;
                    var yy = 16;
                    
                    for (var i = 0; i < string_length(debugDSStackQueueRead); i += 8)
                    {
                        draw_text(xx, debugDSOffsetY + yy, string_copy(debugDSStackQueueRead, 1 + i, 8));
                        xx += 64;
                        
                        if (xx == 384)
                        {
                            xx = 0;
                            yy += 8;
                        }
                    }
                }
            }
            else if (debugDSType == 4)
            {
                draw_text(16, 0, "QUEUE");
                
                if (ds_exists(debugDSID, debugDSType))
                {
                    draw_text(80, 0, "SIZE:" + string(ds_queue_size(debugDSID)));
                    draw_set_color(c_white);
                    
                    if (keyboard_check_pressed(vk_numpad7) || keyboard_check(vk_numpad8))
                        debugDSStackQueueRead = ds_queue_write(debugDSID);
                    
                    draw_set_color(c_yellow);
                    draw_text(0, debugDSOffsetY + 8, "(Press NUMPAD7 or Hold NUMPAD8 to update. May cause lag.");
                    draw_set_color(c_white);
                    var xx = 0;
                    var yy = 16;
                    
                    for (var i = 0; i < string_length(debugDSStackQueueRead); i += 8)
                    {
                        draw_text(xx, debugDSOffsetY + yy, string_copy(debugDSStackQueueRead, 1 + i, 8));
                        xx += 64;
                        
                        if (xx == 384)
                        {
                            xx = 0;
                            yy += 8;
                        }
                    }
                }
            }
            else if (debugDSType == 5)
            {
                draw_text(16, 0, "GRID");
                
                if (ds_exists(debugDSID, debugDSType))
                {
                    draw_text(80, 0, "SIZE:" + string(ds_grid_width(debugDSID)) + "," + string(ds_grid_height(debugDSID)));
                    draw_set_color(c_white);
                    
                    for (var yy = 0; yy < ds_grid_height(debugDSID); yy++)
                    {
                        draw_set_color(c_yellow);
                        draw_text(debugDSOffsetX + 0, debugDSOffsetY + 16 + (yy * 8), yy);
                        draw_set_color(c_white);
                        
                        for (var xx = 0; xx < ds_grid_width(debugDSID); xx++)
                            draw_text(debugDSOffsetX + 24 + (xx * debugDSOffsetWidth), debugDSOffsetY + 16 + (yy * 8), ds_grid_get(debugDSID, xx, yy));
                    }
                    
                    draw_set_color(c_yellow);
                    
                    for (var xx = 0; xx < ds_grid_width(debugDSID); xx++)
                        draw_text(debugDSOffsetX + 24 + (xx * debugDSOffsetWidth), debugDSOffsetY + 8, xx);
                    
                    draw_set_color(c_white);
                }
            }
        }
        
        draw_point(debugCursorX, debugCursorY);
        
    }

    draw_set_font(global.fontDefault);
    
    if (global.CT_MessageTimer2 > current_time)
    {
        draw_set_color(global.palette[0]);
        
        if (global.CT_FeedbackMessages)
            draw_text(0, 209, "SHOW MESSAGES");
        else
            draw_text(0, 209, "HIDE MESSAGES");
    }
    else if (global.CT_MessageTimer > current_time)
    {
        if (global.CT_FeedbackMessages)
        {
            draw_set_color(global.palette[0]);
            draw_text(0, 209, global.CT_MsgText);
        }
    }
    else if (global.STANDARD_FPS != 60)
    {
        draw_set_color(global.palette[2]);
        
        if (!global.paused)
            draw_text(0, 209, string(global.STANDARD_FPS) + " FPS");
    }
    
    var rightSideOffset = 0;
    
    if (global.CT_ShowTasFrameCounter)
    {
        if ((global.INPUT_FRAME + 1) < global.CT_INPUTS_LENGTH)
            draw_set_color(global.palette[11]);
        else
            draw_set_color(global.palette[13]);
        
        draw_set_halign(fa_right);
        draw_text(384, 209 - rightSideOffset, string(global.INPUT_FRAME + 1) + "/" + string(array_length(global.CT_INPUTS)));
        rightSideOffset += 8;
        draw_set_halign(fa_left);
    }
    
    if (global.CT_ShowFrameCounter)
    {
        if (global.paused)
            draw_set_color(global.palette[16]);
        else
            draw_set_color(global.palette[2]);
        
        draw_set_halign(fa_right);
        draw_text(384, 209 - rightSideOffset, global.FRAME_COUNT);
        rightSideOffset += 8;
        draw_set_halign(fa_left);
    }
    
    if (global.CT_BlockSaves)
    {
        if (rightSideOffset == 0)
            draw_sprite(sSaveIconNo, 3, 368, 204);
        else
            draw_sprite(sSaveIconNo, 3, 368, 209 - rightSideOffset);
    }
    
    if (room != rmInit && room != rmLibrary && !global.CT_DisplayInputs && global.STANDARD_FPS == 60)
        draw_sprite(s16_Coffee2, 0, 4, 192);
    
    draw_set_color(c_white);
    surface_reset_target();
}

function applyTasInputs(arg0, arg1)
{
    var player = arg0;
    var inputFrame = arg1;
    
    if (inputFrame < global.CT_INPUTS_LENGTH)
    {
        var c = global.CT_INPUTS[inputFrame];
        
        if (player == 1)
            c = c >> int64(32);

        holdUp        = (c & (int64(1) << int64(0))) != 0;
        pressUp       = (c & (int64(1) << int64(1))) != 0;

        holdDown      = (c & (int64(1) << int64(3))) != 0;
        pressDown     = (c & (int64(1) << int64(4))) != 0;

        holdLeft      = (c & (int64(1) << int64(6))) != 0;
        pressLeft     = (c & (int64(1) << int64(7))) != 0;

        holdRight     = (c & (int64(1) << int64(9))) != 0;
        pressRight    = (c & (int64(1) << int64(10))) != 0;

        fire1         = (c & (int64(1) << int64(12))) != 0;
        fire1pressed  = (c & (int64(1) << int64(13))) != 0;
        fire1released = (c & (int64(1) << int64(14))) != 0;
        fire2         = (c & (int64(1) << int64(15))) != 0;
        fire2pressed  = (c & (int64(1) << int64(16))) != 0;
        fire2released = (c & (int64(1) << int64(17))) != 0;
        pressStart    = (c & (int64(1) << int64(18))) != 0;
    }
    else
    {
        var c = int64(0);

        if (holdUp)        c |= int64(1) << int64(0);
        if (pressUp)       c |= int64(1) << int64(1);

        if (holdDown)      c |= int64(1) << int64(3);
        if (pressDown)     c |= int64(1) << int64(4);

        if (holdLeft)      c |= int64(1) << int64(6);
        if (pressLeft)     c |= int64(1) << int64(7);

        if (holdRight)     c |= int64(1) << int64(9);
        if (pressRight)    c |= int64(1) << int64(10);

        if (fire1)         c |= int64(1) << int64(12);
        if (fire1pressed)  c |= int64(1) << int64(13);
        if (fire1released) c |= int64(1) << int64(14);
        if (fire2)         c |= int64(1) << int64(15);
        if (fire2pressed)  c |= int64(1) << int64(16);
        if (fire2released) c |= int64(1) << int64(17);
        if (pressStart)    c |= int64(1) << int64(18);
        
        while (array_length(global.CT_INPUTS) < (inputFrame + 1))
        {
            var prevInput = int64(0);
            if (array_length(global.CT_INPUTS) > 0)
                prevInput = global.CT_INPUTS[array_length(global.CT_INPUTS) - 1];

            array_push(global.CT_INPUTS, prevInput & int64(0x9249));
        }
        
        if (player == 1)
            global.CT_INPUTS[inputFrame] = (global.CT_INPUTS[inputFrame] & 4294967295) | (c << int64(32));
        else
            global.CT_INPUTS[inputFrame] = (global.CT_INPUTS[inputFrame] & -4294967296) | c;
    }
}

function readTasFile(arg0)
{
    if (file_exists(arg0))
    {
        var b = buffer_load(arg0);
        var magic = buffer_read(b, buffer_u32);
        
        if (magic == TAS_FILE.MAGIC)
        {
            var version = buffer_read(b, buffer_u32);
            var inputsLength = buffer_read(b, buffer_s32);
            var randomizeLength = buffer_read(b, buffer_s32);
            buffer_seek(b, buffer_seek_start, 1024);
            global.CT_INPUTS = array_create(inputsLength);
            global.CT_RANDOMIZE_TABLE = array_create(randomizeLength);
            
            for (var i = 0; i < inputsLength; i++)
                global.CT_INPUTS[i] = buffer_read(b, buffer_u64);
            
            for (var i = 0; i < randomizeLength; i++)
            {
                global.CT_RANDOMIZE_TABLE[i] = array_create(2);
                global.CT_RANDOMIZE_TABLE[i][0] = buffer_read(b, buffer_s32);
                global.CT_RANDOMIZE_TABLE[i][1] = buffer_read(b, buffer_f64);
            }
            
            //Now inputs have been loaded from CTAS file, check for any incongruent inputs originating from older CoffeeTools versions.
            //Runs only when a CTAS is being played back on startup, not when loading a savestate.
            if(global.FRAME_COUNT == 0)
            {
                var incongruentInputsCount = 0;
                for (var player = 0; player < 2; player++)
                {
                    var cprev = global.CT_INPUTS[0];
                    
                    if (player == 1)
                        c = c >> int64(32);
                    
                    for (var inputFrame = 1; inputFrame < inputsLength; inputFrame++)
                    {
                        var c = global.CT_INPUTS[inputFrame];
                        
                        if (player == 1)
                            c = c >> int64(32);
                        
                        for (var input = 0; input < 6; input ++) //UP, DOWN, LEFT, RIGHT, FIRE1, FIRE2
                        {
                            var pressed = (c & power(2,(input*3)+1)) != 0
                            var held = (c & power(2,(input*3)+0)) != 0
                            var released = (c & power(2,(input*3)+2)) != 0
                            var heldprev = (cprev & power(2,(input*3)+0)) != 0
                            
                            if(heldprev and !held and !released) && (input > 3) //Possible for UP, DOWN, LEFT, RIGHT by using a gamepad's stick
                            {
                                incongruentInputsCount ++
                                //global.CT_INPUTS[inputFrame] = global.CT_INPUTS[inputFrame] ^ (1 << ((input*3)+2)) //Flip released flag
                            }
                            //else if (pressed && !released && heldprev) && (false) //Possible for UP, DOWN, LEFT, RIGHT, FIRE1, FIRE2 by using both keyboard and gamepad (Disabled)
                            //{
                            //    incongruentInputsCount ++
                            //    //global.CT_INPUTS[inputFrame] = global.CT_INPUTS[inputFrame] ^ (1 << ((input*3)+2)) //Flip released flag
                            //}
                            else if (!pressed && !held && released && !heldprev) && (input > 3) //Possible for UP, DOWN, LEFT, RIGHT by holding opposite direction
                            {
                                incongruentInputsCount ++
                                //global.CT_INPUTS[inputFrame] = global.CT_INPUTS[inputFrame] ^ (1 << ((input*3)+2)) //Flip released flag
                            }
                            if (!pressed && held && !heldprev) //Possible for UP, DOWN, LEFT, RIGHT by letting go of the opposite direction.
                            {
                                var heldopp = false
                                if(input = 0)
                                    heldopp = (c & power(2,(1*3)+0)) != 0
                                else if(input = 1)
                                    heldopp = (c & power(2,(0*3)+0)) != 0
                                else if(input = 2)
                                    heldopp = (c & power(2,(3*3)+0)) != 0
                                else if(input = 3)
                                    heldopp = (c & power(2,(2*3)+0)) != 0
                                
                                if((input < 4) && (heldopp)) || (input > 3) //If the opposite direction is currently held, then something's wrong.
                                {
                                    incongruentInputsCount ++
                                    //global.CT_INPUTS[inputFrame] = global.CT_INPUTS[inputFrame] ^ (1 << ((input*3)+1)) //Flip pressed flag
                                }
                            }
                        }
                        
                        cprev = c;
                    }
                }
                if(incongruentInputsCount != 0)
                {
                    show_message("Detected "+string(incongruentInputsCount)+" incongruent inputs in CTAS file.")
                }
            }
        }
        
        buffer_delete(b);
    }
    
    global.CT_INPUTS_LENGTH = array_length(global.CT_INPUTS) - 1;
}

function writeTasFile(arg0)
{
    var inputsLength = min(global.INPUT_FRAME + 1, array_length(global.CT_INPUTS));
    var b = buffer_create(1024, buffer_grow, 1);
    buffer_fill(b, 0, buffer_u8, 0, 1024);
    buffer_write(b, buffer_u32, TAS_FILE.MAGIC);
    buffer_write(b, buffer_u32, TAS_FILE.VERSION);
    buffer_write(b, buffer_s32, inputsLength);
    var randomizeLengthOffset = buffer_tell(b);
    buffer_seek(b, buffer_seek_start, 1024);
    
    for (var i = 0; i < inputsLength; i++)
        buffer_write(b, buffer_u64, global.CT_INPUTS[i]);
    
    var randomizeLength = array_length(global.CT_RANDOMIZE_TABLE);
    
    for (var i = 0; i < randomizeLength; i++)
    {
        if (global.CT_RANDOMIZE_TABLE[i][0] > global.INPUT_FRAME)
        {
            randomizeLength = i;
            break;
        }
        
        buffer_write(b, buffer_s32, global.CT_RANDOMIZE_TABLE[i][0]);
        buffer_write(b, buffer_f64, global.CT_RANDOMIZE_TABLE[i][1]);
    }
    
    buffer_seek(b, buffer_seek_start, randomizeLengthOffset);
    buffer_write(b, buffer_s32, randomizeLength);
    buffer_save(b, arg0);
    buffer_delete(b);
}

//These functions will alter the current frame's inputs if they are ever incongruent with the previous frame's inputs (via savestate loading)
function keyboard_check_pressed_verify(player,input)
{
    var pressed = keyboard_check_pressed(global.keyMap[player][input])
    var held = keyboard_check(global.keyMap[player][input])
    var released = keyboard_check_released(global.keyMap[player][input])
    var heldprev = global.PreviousFrameInputs[player][input]
    
    if(!global.CT_FixIncongruentInputsFrame) return pressed;
    
    if (!pressed && held && !heldprev)
    {
        if (DEBUG.MODE == DEBUG.ON) audio_play_sound(sfx_shot23, 0, 0);
        return true;
    }
    else
    {
        return pressed;
    }
}
function keyboard_check_released_verify(player,input)
{
    var pressed = keyboard_check_pressed(global.keyMap[player][input])
    var held = keyboard_check(global.keyMap[player][input])
    var released = keyboard_check_released(global.keyMap[player][input])
    var heldprev = global.PreviousFrameInputs[player][input]
    
    if(!global.CT_FixIncongruentInputsFrame) return released;
    
    if (!held && !released && heldprev)
    {
        if (DEBUG.MODE == DEBUG.ON) audio_play_sound(sfx_shot20, 0, 0);
        return true;
    }
    else if (pressed && !released && heldprev)
    {
        if (DEBUG.MODE == DEBUG.ON) audio_play_sound(sfx_shot21, 0, 0);
        return true;
    }
    else if (!pressed && !held && released && !heldprev)
    {
        if (DEBUG.MODE == DEBUG.ON) audio_play_sound(sfx_shot22, 0, 0);
        return false;
    }
    else
    {
        return released;
    }
}
function gamepad_button_check_pressed_verify_2(slot,player,input,index)
{
    var held = gamepad_button_check(slot, input)
    var pressed = gamepad_button_check_pressed(slot, input)
    var heldprev = global.PreviousFrameInputs[player][index + 6]
    
    if(!global.CT_FixIncongruentInputsFrame) return pressed;
    
    var correctedPressed = held && !heldprev;
    if (pressed != correctedPressed)
    {
        if (DEBUG.MODE == DEBUG.ON) audio_play_sound(sfx_shot04, 0, 0);
    }
    return correctedPressed;
}
function gamepad_button_check_released_verify_2(slot,player,input,index)
{
    var held = gamepad_button_check(slot, input)
    var released = gamepad_button_check_released(slot, input)
    var heldprev = global.PreviousFrameInputs[player][index + 6]
    
    if(!global.CT_FixIncongruentInputsFrame) return released;
    
    var correctedReleased = !held && heldprev;
    if (released != correctedReleased)
    {
        if (DEBUG.MODE == DEBUG.ON) audio_play_sound(sfx_shot01, 0, 0);
    }
    return correctedReleased;
}
function gamepad_button_check_pressed_verify(slot,player,input)
{
    return gamepad_button_check_pressed_verify_2(slot,player,global.joyMap[player][input],input);
}
function gamepad_button_check_released_verify(slot,player,input)
{
    return gamepad_button_check_released_verify_2(slot,player,global.joyMap[player][input],input);
}

enum LOG
{
    NONE,
    INFO,
    VERBOSE,

    LEVEL = 1
}

enum DEBUG
{
    OFF = 0,
    ON = 1,

    MODE = 0
}

enum C
{
    AUTO_REPEAT_INITIAL_DELAY = 500,
    AUTO_REPEAT_DELAY = 31,

    DS_COUNT = 1024
}

enum APPEND_FILE
{
    MAGIC = 0x46415443,
    VERSION = 2,

    HEADER_LENGTH = 1024
}

enum TAS_FILE
{
    MAGIC = 0x53415443,
    VERSION = 3,

    HEADER_LENGTH = 1024
}

enum e__VW
{
    XView,
    YView,
    WView,
    HView,
    Angle,
    HBorder,
    VBorder,
    HSpeed,
    VSpeed,
    Object,
    Visible,
    XPort,
    YPort,
    WPort,
    HPort,
    Camera,
    SurfaceID
}
