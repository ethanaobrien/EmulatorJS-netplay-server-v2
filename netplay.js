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
        try {
            site = (new URL(requestedSite)).host;
        } catch(e) {
            console.warn("Could not set url to \""+requestedSite+"\". There was an error parsing the URL. Using "+window.location.host);
            site = window.location.host;
        }
    } else {
        site = window.location.host;
    }
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
        await this.openSocket(socketURL);
        if (this.opts.createRoom) {
            this.createRoom(this.opts.room, this.opts.name, this.opts.id, this.opts.site);
        } else {
            this.joinRoom(this.opts.room, this.opts.name, this.opts.id, this.opts.site);
        }
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
            this.socket.addEventListener('open', resolve);
            this.socket.addEventListener('close', this.onClose);
            this.socket.addEventListener('message', this.onMessage);
        })
    },
    onClose: function(e) {
        this.callListener("close");
    },
    onMessage: function(e) {
        console.log(e);
        if (e.data && this.joined === false && typeof e.data === "string") {
            if (e.data === "Connected") {
                this.joined = true;
                this.callListener("connected");
                this.users.push(this.opts.name);
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
                const name = e.data.substring(16).trim();
                if (this.listeners.userconnection) {
                    this.listeners.userconnection({name:name});
                }
                this.users.push(name);
            } else if (e.data.startsWith("User Disonnected")) {
                const name = e.data.substring(18).trim();
                if (this.listeners.userdisconnection) {
                    this.listeners.userdisconnection({name:name});
                }
                const index = array.indexOf(name);
                if (index > -1) {
                    this.users.splice(index, 1);
                }
            } else if (e.data.startsWith("keydown:")) {
                let data;
                try {
                    data = JSON.parse(e.data.substring(8));
                } catch(e) {};
                if (!data) {
                    console.warn("error parsing incoming keydown data.", e.data);
                }
                if (this.listeners.keydown) {
                    this.listeners.keydown(data);
                }
            }
            //Todo - restart game, load/save state, pause/play game, etc...
        }
    },
    //data - json string of data to be called as an event on the other users
    keyDown: function(data) {
        this.socket.send("keydown:"+JSON.stringify(data));
    },
    users: [],
    getUsers: function() {
        return this.users;
    },
    //This function is to be set as Module.postMainLoop - will keep the players in sync
    //All this is based off the user that owns the room.
    //This function keeps the inputs in sync with the frames - so chances are I'll need to re-write the way I do inputs.
    postMainLoop: function() {
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
