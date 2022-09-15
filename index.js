const socket = new WebSocket("wss://s2.zloserver.com:41065/healthCheck/students/bot");
let elem = document.getElementById('text')

socket.onmessage = (event) => {
    elem.textContent = "Hello, I'm a bot for Students and I'm " + (event.data.split(':')[1] ? "Alive" : "Dead")
}

socket.onopen = () => {
    setInterval(() => {
        socket.send("ping");
    }, 1000)
}

socket.onclose = () => {
    elem.textContent = "Hello, I'm a bot for Students and I'm Dead"
}