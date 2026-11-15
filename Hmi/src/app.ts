// HMI stanowiska silnika. Ekran tylko wyświetla stan i wysyła polecenia:
// nie zna Modbusa i nie podejmuje decyzji sterowania.
// Kontrakt wiadomości: docs/websocket.md (po stronie C#: Shared/Messages.cs).

type PlcState = "stopped" | "starting" | "running" | "braking" | "estop" | "fault";
type Alarm = "none" | "overheat" | "hmiCommLost" | "driveCommLost";
type CommandName = "start" | "stop" | "estop" | "reset" | "setSpeed";

type Command = { cmd: CommandName; value?: number };

type Status = {
  type: "status";
  state: PlcState;
  rpm: number;
  temperatureCelsius: number;
  alarm: Alarm;
  speedSetpoint: number;
  running: boolean;
  estopLatched: boolean;
  fault: boolean;
};

type Link = { type: "link"; plcConnected: boolean };

type CommandLogEntry = {
  time: string;
  cmd: string;
  value?: number;
  source: string;
  result: "sent" | "rejected" | "error";
  stateBefore?: PlcState;
  detail?: string;
};

type CommandLog = { type: "commandLog"; entries: CommandLogEntry[] };
type CommandEvent = { type: "command"; entry: CommandLogEntry };
type ServerMessage = Status | Link | CommandLog | CommandEvent;

const MAX_RPM = 3000;
const MAX_TEMP = 120;
const ALARM_TEMP = 80;
const LOG_ROWS = 100;
const CHART_SECONDS = 60;
const GAUGE_ARC = Math.PI * 80; // długość łuku wskaźnika (półokrąg o promieniu 80)

const STATE_TEXT: Record<PlcState, string> = {
  stopped: "STOI",
  starting: "ROZPĘDZA SIĘ",
  running: "PRACUJE",
  braking: "HAMUJE",
  estop: "E-STOP",
  fault: "AWARIA",
};

const ALARM_TEXT: Record<Alarm, string> = {
  none: "",
  overheat: "Przegrzanie silnika",
  hmiCommLost: "Brak komunikacji z HMI",
  driveCommLost: "Brak komunikacji z napędem",
};

const CMD_TEXT: Record<string, string> = {
  start: "Start",
  stop: "Stop",
  estop: "E-STOP",
  reset: "Reset",
  setSpeed: "Nastawa",
};

const RESULT_TEXT: Record<CommandLogEntry["result"], string> = {
  sent: "wysłano",
  rejected: "odrzucono",
  error: "błąd",
};

function el<T extends Element = HTMLElement>(id: string): T {
  const found = document.getElementById(id);
  if (!found) throw new Error(`Brak elementu #${id}`);
  return found as unknown as T;
}

const linkWs = el("link-ws");
const linkPlc = el("link-plc");
const alarmBox = el("alarm");
const stateBox = el("state");
const lampRun = el("lamp-run");
const lampEstop = el("lamp-estop");
const lampFault = el("lamp-fault");
const rpmText = el("rpm");
const tempText = el("temp");
const setpointNow = el("setpoint-now");
const gaugeRpm = el<SVGPathElement>("g-rpm");
const gaugeTemp = el<SVGPathElement>("g-temp");
const rotor = el<SVGGElement>("rotor");
const chart = el<HTMLCanvasElement>("chart");
const logBody = el<HTMLTableSectionElement>("log");
const logCount = el("log-count");
const speedInput = el<HTMLInputElement>("speed");
const speedRange = el<HTMLInputElement>("speed-range");
const buttons = Array.from(document.querySelectorAll<HTMLButtonElement>("button"));

let socket: WebSocket | null = null;
let liveRpm = 0; // obroty do animacji wirnika; 0, gdy nie mamy świeżych danych
const samples: { t: number; rpm: number; temp: number }[] = [];

// ---------- Połączenie z bramą ----------

function connect(): void {
  const scheme = location.protocol === "https:" ? "wss" : "ws";
  const ws = new WebSocket(`${scheme}://${location.host}/ws`);
  socket = ws;

  ws.onopen = () => {
    setLink(linkWs, true);
    setButtonsEnabled(true);
  };

  ws.onmessage = (event) => {
    const message = JSON.parse(event.data as string) as ServerMessage;
    switch (message.type) {
      case "status": showStatus(message); break;
      case "link": if (!message.plcConnected) showNoData("Brama nie ma połączenia z PLC"); break;
      case "commandLog": showLog(message.entries); break;
      case "command": addLogRow(message.entry); break;
    }
  };

  ws.onclose = () => {
    socket = null;
    setLink(linkWs, false);
    setLink(linkPlc, null);
    setButtonsEnabled(false);
    showNoData("Brak połączenia z bramą");
    setTimeout(connect, 1000);
  };
}

function send(command: Command): void {
  if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify(command));
}

// ---------- Wyświetlanie stanu ----------

function showStatus(s: Status): void {
  setLink(linkPlc, true);

  stateBox.textContent = STATE_TEXT[s.state] ?? String(s.state);
  stateBox.className = `state s-${s.state}`;

  lampRun.classList.toggle("on", s.running);
  lampEstop.classList.toggle("on", s.estopLatched);
  lampFault.classList.toggle("on", s.fault);

  rpmText.textContent = String(s.rpm);
  tempText.textContent = s.temperatureCelsius.toFixed(1);
  setpointNow.textContent = `${s.speedSetpoint} obr/min`;
  setGauge(gaugeRpm, s.rpm / MAX_RPM);
  setGauge(gaugeTemp, s.temperatureCelsius / MAX_TEMP);
  gaugeTemp.classList.toggle("hot", s.temperatureCelsius > ALARM_TEMP);
  setGaugeMark("g-rpm-setpoint", s.speedSetpoint / MAX_RPM);

  if (s.alarm === "none") {
    alarmBox.hidden = true;
  } else {
    alarmBox.hidden = false;
    alarmBox.className = "alarm";
    alarmBox.textContent = `ALARM: ${ALARM_TEXT[s.alarm] ?? s.alarm}. Po usunięciu przyczyny naciśnij Reset.`;
  }

  liveRpm = s.rpm;
  samples.push({ t: Date.now(), rpm: s.rpm, temp: s.temperatureCelsius });
  drawChart();
}

// Bez świeżych danych nie pokazujemy starych wartości jako aktualnych.
function showNoData(reason: string): void {
  if (reason.includes("PLC")) setLink(linkPlc, false);
  stateBox.textContent = "BRAK DANYCH";
  stateBox.className = "state";
  for (const lamp of [lampRun, lampEstop, lampFault]) lamp.classList.remove("on");
  rpmText.textContent = "—";
  tempText.textContent = "—";
  setpointNow.textContent = "—";
  setGauge(gaugeRpm, 0);
  setGauge(gaugeTemp, 0);
  alarmBox.hidden = false;
  alarmBox.className = "alarm warn";
  alarmBox.textContent = reason;
  liveRpm = 0;
}

function setLink(element: HTMLElement, up: boolean | null): void {
  element.classList.toggle("up", up === true);
  element.classList.toggle("down", up === false);
}

function setButtonsEnabled(enabled: boolean): void {
  for (const button of buttons) button.disabled = !enabled;
}

function setGauge(path: SVGPathElement, fraction: number): void {
  const f = Math.min(1, Math.max(0, fraction));
  path.style.strokeDasharray = `${f * GAUGE_ARC} ${GAUGE_ARC}`;
}

function setGaugeMark(id: string, fraction: number): void {
  const f = Math.min(1, Math.max(0, fraction));
  // 0 = lewy koniec łuku (-90°), 1 = prawy koniec (+90°); kreska bazowo stoi na górze.
  el<SVGLineElement>(id).setAttribute("transform", `rotate(${-90 + 180 * f} 100 105)`);
}

// ---------- Animacja wirnika ----------

let angle = 0;
let lastFrame = performance.now();

function animate(now: number): void {
  const dt = (now - lastFrame) / 1000;
  lastFrame = now;
  // Zwolnione 25 razy, żeby obrót było widać (1500 obr/min = 1 obrót na sekundę na ekranie).
  angle = (angle + (liveRpm / 60) * 360 * dt / 25) % 360;
  rotor.setAttribute("transform", `rotate(${angle.toFixed(1)} 90 85)`);
  requestAnimationFrame(animate);
}

// ---------- Wykres ----------

function drawChart(): void {
  const cutoff = Date.now() - CHART_SECONDS * 1000;
  while (samples.length > 0 && samples[0]!.t < cutoff) samples.shift();

  const ratio = window.devicePixelRatio || 1;
  const width = chart.clientWidth;
  const height = chart.clientHeight;
  if (chart.width !== width * ratio || chart.height !== height * ratio) {
    chart.width = width * ratio;
    chart.height = height * ratio;
  }
  const ctx = chart.getContext("2d");
  if (!ctx) return;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  ctx.clearRect(0, 0, width, height);

  // siatka: 4 poziome linie
  ctx.strokeStyle = "#2c343d";
  ctx.lineWidth = 1;
  for (let i = 1; i < 4; i++) {
    const y = (height * i) / 4;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }

  const x = (t: number) => ((t - cutoff) / (CHART_SECONDS * 1000)) * width;
  const line = (color: string, value: (p: { rpm: number; temp: number }) => number, max: number) => {
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.beginPath();
    samples.forEach((p, i) => {
      const y = height - 4 - (Math.min(value(p), max) / max) * (height - 8);
      if (i === 0) ctx.moveTo(x(p.t), y);
      else ctx.lineTo(x(p.t), y);
    });
    ctx.stroke();
  };
  line("#58a6ff", (p) => p.rpm, MAX_RPM);
  line("#d29922", (p) => p.temp, MAX_TEMP);
}

// ---------- Dziennik poleceń ----------

function showLog(entries: CommandLogEntry[]): void {
  logBody.replaceChildren();
  for (const entry of entries) addLogRow(entry);
}

// Najnowszy wpis na górze; trzymamy najwyżej LOG_ROWS wierszy.
function addLogRow(entry: CommandLogEntry): void {
  const row = document.createElement("tr");
  const time = new Date(entry.time);

  const command = entry.cmd === "setSpeed" && entry.value !== undefined
    ? `Nastawa ${entry.value} obr/min`
    : (CMD_TEXT[entry.cmd] ?? entry.cmd);
  const result = entry.detail
    ? `${RESULT_TEXT[entry.result] ?? entry.result}: ${entry.detail}`
    : (RESULT_TEXT[entry.result] ?? entry.result);

  // textContent, nie innerHTML: treść wpisów pochodzi z sieci.
  cell(row, time.toLocaleTimeString("pl-PL"), "muted").title = time.toLocaleString("pl-PL");
  cell(row, command, entry.cmd === "estop" ? "c-estop" : "");
  cell(row, entry.stateBefore ? STATE_TEXT[entry.stateBefore].toLowerCase() : "—", "muted");
  cell(row, result, `r-${entry.result}`);
  cell(row, entry.source, "muted");

  logBody.prepend(row);
  while (logBody.rows.length > LOG_ROWS) logBody.deleteRow(-1);
  logCount.textContent = String(logBody.rows.length);
}

function cell(row: HTMLTableRowElement, text: string, className: string): HTMLTableCellElement {
  const td = row.insertCell();
  td.textContent = text;
  if (className) td.className = className;
  return td;
}

// ---------- Przyciski ----------

el("btn-start").addEventListener("click", () => send({ cmd: "start" }));
el("btn-stop").addEventListener("click", () => send({ cmd: "stop" }));
el("btn-reset").addEventListener("click", () => send({ cmd: "reset" }));
el("btn-estop").addEventListener("click", () => send({ cmd: "estop" }));

speedRange.addEventListener("input", () => { speedInput.value = speedRange.value; });
speedInput.addEventListener("input", () => { speedRange.value = speedInput.value; });
el<HTMLFormElement>("speed-form").addEventListener("submit", (event) => {
  event.preventDefault();
  const value = Math.round(Number(speedInput.value));
  if (Number.isFinite(value)) send({ cmd: "setSpeed", value });
});

setGaugeMark("g-temp-limit", ALARM_TEMP / MAX_TEMP);
setButtonsEnabled(false);
showNoData("Łączenie z bramą…");
requestAnimationFrame(animate);
connect();
