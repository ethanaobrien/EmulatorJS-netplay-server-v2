function EJS_NETPLAY(createRoom, room, name, socketURL, id, requestedSite) {
    if ([true,false,1,0].indexOf(createRoom) === -1) {
        throw new TypeError("Invalid value for option createRoom. Allowed options are: true, false, 0, and 1.");
    }
    if (typeof room !== "string") {
        throw new TypeError("Invalid type for option room. Allowed types are String.");
    }
    room = room.trim();
    if (room.length === 0) {
        throw new TypeError("Invalid value for option room. Length (trimmed) must be greater than 0.");
    }
    if (typeof name !== "string") {
        throw new TypeError("Invalid type for option name. Allowed types are String.");
    }
    name = name.trim();
    if (name.length === 0) {
        throw new TypeError("Invalid value for option name. Length (trimmed) must be greater than 0.");
    }
    const socketUrl = new URL(socketURL); //This will throw the error for us
    if (["wss:", "ws:"].indexOf(socketUrl.protocol) === -1) {
        throw new TypeError("Invalid value for option socketURL. Protocol must be 'wss:' or 'ws:'.");
    }
    if (typeof id !== 'number') {
        throw new TypeError("Invalid type for argument gameID. Allowed types are Number.");
    }
    let site;
    if (requestedSite) {
        if (typeof requestedSite !== "string") {
            console.warn("Invalid type for argument requestedSite. Using "+window.location.host);
            site = window.location.host;
        } else {
            site = requestedSite;
        }
    } else {
        site = window.location.host;
    }
    this.owner = createRoom;
    this.opts = {
        createRoom,
        room,
        name,
        socketURL,
        id,
        site
    }
}

EJS_NETPLAY.prototype = {
    init: async function() {
        this.joined = false;
        this.error = false;
        await this.openSocket(this.opts.socketURL);
        if (this.opts.createRoom) {
            this.createRoom(this.opts.room, this.opts.name, this.opts.id, this.opts.site);
        } else {
            this.joinRoom(this.opts.room, this.opts.name, this.opts.id, this.opts.site);
        }
    },
    paused: false,
    pause: function() {
        this.listeners.pause();
        this.paused = true;
    },
    play: function() {
        this.listeners.play();
        this.paused = false;
    },
    listeners: {},
    callListener: function(type) {
        if (this.listeners[type.toLowerCase()]) {
            this.listeners[type.toLowerCase()]();
        }
    },
    error: function() {
        this.callListener('error');
        try {
            this.socket.close();
        } catch(e) {}
    },
    on: function(event, cb) {
        if (typeof event !== 'string') {
            throw new TypeError("Invalid type for event argument. Allowed types are String.");
        }
        if (typeof cb !== 'function') {
            throw new TypeError("Invalid type for cb argument. Allowed types are Function.");
        }
        this.listeners[event.toLowerCase()] = cb;
    },
    addEventListener: function(event, cb) {
        this.on(event, cb);
    },
    openSocket: function(socketURL) {
        return new Promise((resolve, reject) => {
            this.socket = new WebSocket(socketURL);
            this.socket.binaryType = "arraybuffer";
            this.socket.addEventListener('open', resolve);
            this.socket.addEventListener('error', function(e) {console.warn(e)});
            this.socket.addEventListener('close', this.onClose.bind(this));
            this.socket.addEventListener('message', this.onMessage.bind(this));
        })
    },
    onClose: function(e) {
        this.callListener("close");
    },
    quit: function() {
        this.socket.close();
    },
    incomingSaveState: null,
    onMessage: async function(e) {
        //console.log(e);
        if (e.data && this.joined === false && typeof e.data === "string") {
            if (e.data === "Connected") {
                this.joined = true;
                this.users.push(this.opts.name);
                this.callListener("connected");
                if (this.owner) this.checkFrameSync();
                else setInterval(this.sendFrameNumber.bind(this), 100);
            } else if (e.data === "Error Connecting") {
                this.joined = false;
                this.error = true; //Todo - tell the user what errored
                this.error();
            } else {
                this.error = true;
                this.error();
            }
            return;
        } else if (this.joined === false) {
            this.error = true;
            this.error();
            return;
        } else if (e.data && typeof e.data === "string") {
            if (e.data.startsWith("User Connected")) {
                if (this.owner) { //Lets have the owner keep track of all this stuff
                    const name = e.data.substring(16).trim();
                    this.callListener("userdatachanged");
                    this.users.push(name);
                    this.socket.send("userDataChanged:"+JSON.stringify(this.users));
                    const state = await this.listeners.savestate();
                    this.socket.send("incoming_save_state:"+state.byteLength);
                    this.socket.send(state);
                }
            } else if (e.data.startsWith("User Disonnected")) {
                if (this.owner) {
                    const name = e.data.substring(18).trim();
                    this.callListener("userdatachanged");
                    const index = array.indexOf(name);
                    if (index > -1) {
                        this.users.splice(index, 1);
                    }
                    this.socket.send("userDataChanged:"+JSON.stringify(this.users));
                }
            } else if (e.data.startsWith("userDataChanged:")) {
                let data;
                try {
                    data = JSON.parse(e.data.substring(16));
                } catch(e) {
                    console.warn("Error parsing updated user data.");
                    return;
                }
                this.users = data;
                this.callListener("userdatachanged");
            } else if (e.data.startsWith("sync-control:")) {
                let data;
                const userNum = this.users.indexOf(this.opts.name);
                try {
                    data = JSON.parse(e.data.substring(13));
                } catch(e) {
                    console.warn("Error parsing sync control data.");
                    return;
                }
                this.pendingInputs.push(data);
                //if (this.listeners.keydown) {
                //    this.listeners.keydown(data);
                //}
            } else if (e.data.startsWith('incoming_save_state')) {
                this.incomingSaveState = {length: parseInt(e.data.substring(20)), data:[], recieved:0};
            } else if (e.data.startsWith("current-frame")) {
                let data;
                try {
                    data = JSON.parse(e.data.substring(14));
                } catch(e) {
                    console.warn("Error parsing sync frame data.");
                    return;
                }
                this.userFrames[data.user] = data.frame;
            } else if (e.data.startsWith("short-pause:")) {
                if (parseInt(e.data.substring(12).split("|")[0]) === this.users.indexOf(this.opts.name)) {
                    this.pause();
                    setTimeout(this.play.bind(this), parseInt(e.data.substring(12).split("|")[1]));
                }
            }
            //Todo - restart game, load/save state, pause/play game, etc...
        } else if (e.data && typeof e.data !== 'string') {
            if (this.incomingSaveState !== null) {
                this.incomingSaveState.data.push(e.data);
                this.incomingSaveState.recieved += e.data.byteLength;
                if (this.incomingSaveState.recieved >= this.incomingSaveState.length) {
                    const state = new Uint8Array(await (new Blob(this.incomingSaveState.data)).arrayBuffer());
                    this.listeners.loadstate(state);
                    this.pendingInputs = [];
                    this.currentFrame = 0;
                    this.incomingSaveState = null;
                }
            }
        }
    },
    keyChanged: function(data) {
        let message = {};
        message.data = data;
        message.frame = this.currentFrame;
        message.player = this.users.indexOf(this.opts.name);
        this.socket.send("sync-control:"+JSON.stringify(message));
    },
    pendingInputs: [],
    users: [],
    userFrames: [null, null, null, null],
    getUsers: function() {
        return this.users;
    },
    checkFrameSync: async function() {
        if (this.paused) {
            setTimeout(this.checkFrameSync.bind(this), 100);
            return;
        }
        for (let i=0; i<this.userFrames.length; i++) {
            if (this.userFrames[i] === null) continue;
            if (!this.users[i]) {
                this.userFrames[i] = null;
                continue;
            }
            const diff = this.currentFrame - this.userFrames[i];
            if (diff < 100) {
                this.socket.send("short-pause:"+i+"|"+(-(diff-100)));
            } else if (diff > 500) {
                const state = await this.listeners.savestate();
                this.socket.send("incoming_save_state:"+state.byteLength);
                this.socket.send(state);
            }
        }
        setTimeout(this.checkFrameSync.bind(this), 100);
    },
    sendFrameNumber: function() {
        if (this.paused) return;
        this.socket.send("current-frame:"+JSON.stringify({frame:this.currentFrame, user:this.users.indexOf(this.opts.name)}));
    },
    currentFrame: 0,
    postMainLoop: function() {
        if (this.paused) return;
        this.currentFrame++;
        
        for (let i=0; i<this.pendingInputs.length; i++) {
            if (this.pendingInputs[i].frame <= this.currentFrame) {
                if (this.listeners.keychanged) {
                    this.listeners.keychanged(this.pendingInputs[i]);
                }
                this.pendingInputs.splice(i, 1);
                i--;
            }
        }
        
        //Todo - I have no clue how to even start - Chances are I'll be referencing the old code.
        //See https://github.com/ethanaobrien/emulatorjs/blob/ae1d574242421e1d8dc01b3b25720d62881fa7cc/data/emu-main.js#L3195
    },
    createRoom: function(roomName, userName, id, site) {
        this.socket.send('OpenRoom\n'+roomName+'\n'+userName+'\n'+site+'\n'+id);
    },
    joinRoom: function(roomName, userName, id, site) {
        this.socket.send('JoinRoom\n'+roomName+'\n'+userName+'\n'+site+'\n'+id);
    }
}
