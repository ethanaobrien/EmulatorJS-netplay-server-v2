function EJS_NETPLAY(create, name, user, socketURL, id, site) {
    if (create===undefined||
        name===undefined||
        user===undefined||
        socketURL===undefined||
        id===undefined||
        site===undefined) {
        throw new Error("Missing one or more needed values");
    };
    (async () => {
        await this.openSocket(socketURL);
        if (create) {
            this.createRoom(name, user, id, site);
        } else {
            this.joinRoom(name, user, id, site);
        }
    })();
}

EJS_NETPLAY.prototype = {
    openSocket: function(socketURL) {
        return new Promise((resolve, reject) => {
            this.socket = new WebSocket(socketURL);
            this.socket.addEventListener('open', resolve);
            this.socket.addEventListener('message', this.onMessage);
        })
    },
    onMessage: function(e) {
        console.log(e);
    },
    createRoom: function(roomName, userName, id, site) {
        this.socket.send('OpenRoom\n'+roomName+'\n'+userName+'\n'+site+'\n'+id);
    },
    joinRoom: function(roomName, userName, id, site) {
        this.socket.send('JoinRoom\n'+roomName+'\n'+userName+'\n'+site+'\n'+id);
    }
}
