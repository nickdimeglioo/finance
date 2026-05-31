#!/usr/bin/env python3
"""
Pipeline Task Manager
Run: python app.py
CLI: python app.py --list --format json
Data stored in tasks.db (auto-created in same directory)

Bulk apply format (CSV): insert_type,id,title,description,status,priority,effort,scope,phase
Bulk apply format (JSON): [{"insert_type":"insert","title":...,"priority":1,"status":1,...}, ...]
"""

import sys
import os
import json
import sqlite3
import csv
import io
import argparse
from datetime import datetime
from pathlib import Path

try:
    from PyQt5.QtWidgets import (
        QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
        QTableWidget, QTableWidgetItem, QLabel, QPushButton, QLineEdit,
        QComboBox, QSpinBox, QTextEdit, QDialog, QDialogButtonBox,
        QSplitter, QFrame, QScrollArea, QCheckBox, QGroupBox,
        QTabWidget, QFileDialog, QMessageBox, QHeaderView,
        QAbstractItemView, QStatusBar, QFormLayout, QGridLayout,
        QSizePolicy, QMenu, QAction, QListWidget, QListWidgetItem,
        QStyledItemDelegate, QStyleOptionViewItem
    )
    from PyQt5.QtCore import Qt, QSortFilterProxyModel, QSize, pyqtSignal, QTimer
    from PyQt5.QtGui import (
        QColor, QFont, QPalette, QPixmap, QPainter, QBrush,
        QPen, QFontDatabase, QLinearGradient, QIcon
    )
    GUI_AVAILABLE = True
except ImportError:
    GUI_AVAILABLE = False

    class _DummyMeta(type):
        def __getattr__(cls, name):
            return 0

    class _Dummy(metaclass=_DummyMeta):
        def __init__(self, *args, **kwargs):
            pass

        def __call__(self, *args, **kwargs):
            return self

        def __getattr__(self, name):
            return self

        def __iter__(self):
            return iter(())

        def __and__(self, other):
            return self

        def __or__(self, other):
            return self

        def __invert__(self):
            return self

    class _Qt:
        def __getattr__(self, name):
            return 0

    def pyqtSignal(*args, **kwargs):
        return _Dummy()

    Qt = _Qt()
    QApplication = QMainWindow = QWidget = QVBoxLayout = QHBoxLayout = _Dummy
    QTableWidget = QTableWidgetItem = QLabel = QPushButton = QLineEdit = _Dummy
    QComboBox = QSpinBox = QTextEdit = QDialog = QDialogButtonBox = _Dummy
    QSplitter = QFrame = QScrollArea = QCheckBox = QGroupBox = _Dummy
    QTabWidget = QFileDialog = QMessageBox = QHeaderView = _Dummy
    QAbstractItemView = QStatusBar = QFormLayout = QGridLayout = _Dummy
    QSizePolicy = QMenu = QAction = QListWidget = QListWidgetItem = _Dummy
    QStyledItemDelegate = QStyleOptionViewItem = QSortFilterProxyModel = _Dummy
    QSize = QTimer = QColor = QFont = QPalette = QPixmap = QPainter = _Dummy
    QBrush = QPen = QFontDatabase = QLinearGradient = QIcon = _Dummy

# ─── PATHS ───────────────────────────────────────────────────────────────────

APP_DIR = Path(__file__).parent
DB_PATH = APP_DIR / "tasks.db"

# ─── CONFIG ──────────────────────────────────────────────────────────────────

STATUS_CONFIG = {
    1: {"name": "Not Started",  "short": "NOT STARTED",  "color": "#64748B", "bg": "#F1F5F9", "dot": "○"},
    2: {"name": "In Progress",  "short": "IN PROGRESS",  "color": "#2563EB", "bg": "#DBEAFE", "dot": "◔"},
    3: {"name": "Started",      "short": "STARTED",      "color": "#D97706", "bg": "#FEF3C7", "dot": "◑"},
    4: {"name": "Kinda Done",   "short": "KINDA DONE",   "color": "#7C3AED", "bg": "#EDE9FE", "dot": "◕"},
    5: {"name": "Done",         "short": "DONE",         "color": "#059669", "bg": "#D1FAE5", "dot": "●"},
}

SCOPE_OPTIONS = ["Backend", "Frontend", "Agent", "DB", "Compiler", "All"]

def priority_style(p):
    if p <= 3:   return "#DC2626", "#FEF2F2"
    if p <= 6:   return "#D97706", "#FFFBEB"
    return "#059669", "#ECFDF5"

def priority_label(p):
    if p <= 3:  return "Critical"
    if p <= 6:  return "Medium"
    return "Low"

def ts_fmt(iso):
    try:
        return datetime.fromisoformat(iso).strftime("%b %d, %H:%M")
    except Exception:
        return iso

# ─── DATABASE ────────────────────────────────────────────────────────────────

class DB:
    _instance = None
    _db_path = DB_PATH

    def __init__(self, db_path=None):
        self.db_path = Path(db_path or self._db_path)
        self.conn = sqlite3.connect(str(self.db_path), check_same_thread=False)
        self.conn.row_factory = sqlite3.Row
        self.conn.execute("PRAGMA foreign_keys = ON")
        self._init_schema()

    @classmethod
    def configure_path(cls, db_path):
        cls._db_path = Path(db_path)
        cls._instance = None

    @classmethod
    def get(cls):
        if cls._instance is None:
            cls._instance = cls()
        return cls._instance

    def _init_schema(self):
        self.conn.executescript("""
            CREATE TABLE IF NOT EXISTS tasks (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                title       TEXT NOT NULL,
                description TEXT DEFAULT '',
                status      INTEGER DEFAULT 1,
                priority    INTEGER DEFAULT 5,
                effort      INTEGER DEFAULT 0,
                scope       TEXT DEFAULT '',
                phase       TEXT DEFAULT '',
                created_at  TEXT NOT NULL,
                updated_at  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS comments (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id    INTEGER NOT NULL,
                content    TEXT NOT NULL,
                author     TEXT DEFAULT 'You',
                created_at TEXT NOT NULL,
                FOREIGN KEY(task_id) REFERENCES tasks(id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS audit_log (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id    INTEGER,
                task_title TEXT,
                action     TEXT NOT NULL,
                field      TEXT,
                old_value  TEXT,
                new_value  TEXT,
                timestamp  TEXT NOT NULL
            );
        """)
        self.conn.commit()

    def _now(self):
        return datetime.now().isoformat()

    def _audit(self, task_id, task_title, action, field=None, old_value=None, new_value=None):
        self.conn.execute(
            "INSERT INTO audit_log (task_id,task_title,action,field,old_value,new_value,timestamp) VALUES (?,?,?,?,?,?,?)",
            (task_id, task_title, action, field, old_value, new_value, self._now())
        )
        self.conn.commit()

    def add_task(self, title, description='', status=1, priority=5,
                 effort=0, scope='', phase=''):
        if not str(title).strip():
            raise ValueError("title is required for inserted tasks")
        now = self._now()
        cur = self.conn.execute(
            "INSERT INTO tasks (title,description,status,priority,effort,scope,phase,created_at,updated_at) "
            "VALUES (?,?,?,?,?,?,?,?,?)",
            (str(title).strip(), description, int(status), int(priority), int(effort), scope, phase, now, now)
        )
        self.conn.commit()
        tid = cur.lastrowid
        self._audit(tid, title, 'created',
                    new_value=f"P{priority} | {STATUS_CONFIG.get(status,{}).get('name','')}")
        return tid

    def update_task(self, task_id, **kwargs):
        old = self.get_task(task_id)
        if not old:
            return False
        kwargs = {k: v for k, v in kwargs.items() if v is not None}
        if not kwargs:
            return True
        for numeric_field in ("status", "priority", "effort"):
            if numeric_field in kwargs:
                kwargs[numeric_field] = int(kwargs[numeric_field])
        kwargs['updated_at'] = self._now()
        set_clause = ', '.join(f"{k}=?" for k in kwargs)
        self.conn.execute(f"UPDATE tasks SET {set_clause} WHERE id=?",
                          list(kwargs.values()) + [task_id])
        self.conn.commit()
        for field, new_val in kwargs.items():
            if field == 'updated_at':
                continue
            old_val = old.get(field)
            if str(old_val) != str(new_val):
                if field == 'status':
                    ov = STATUS_CONFIG.get(old_val, {}).get('name', str(old_val))
                    nv = STATUS_CONFIG.get(new_val, {}).get('name', str(new_val))
                else:
                    ov, nv = str(old_val) if old_val else '', str(new_val)
                self._audit(task_id, old.get('title', ''), 'updated',
                            field=field, old_value=ov, new_value=nv)
        return True

    def get_task(self, task_id):
        row = self.conn.execute("SELECT * FROM tasks WHERE id=?", (task_id,)).fetchone()
        return dict(row) if row else None

    def delete_task(self, task_id):
        task = self.get_task(task_id)
        if task:
            self._audit(task_id, task['title'], 'deleted')
        else:
            return False
        self.conn.execute("DELETE FROM tasks WHERE id=?", (task_id,))
        self.conn.commit()
        return True

    def get_tasks(self, status_filter=0, scope_filter='', search='',
                  sort_col='priority', sort_asc=True):
        q = "SELECT * FROM tasks WHERE 1=1"
        p = []
        if status_filter:
            q += " AND status=?"; p.append(status_filter)
        if scope_filter:
            q += " AND scope LIKE ?"; p.append(f"%{scope_filter}%")
        if search:
            q += " AND (title LIKE ? OR description LIKE ? OR phase LIKE ?)"; p.extend([f"%{search}%"]*3)
        valid_cols = {'priority','status','title','effort','created_at','updated_at','phase'}
        if sort_col not in valid_cols:
            sort_col = 'priority'
        q += f" ORDER BY {sort_col} {'ASC' if sort_asc else 'DESC'}"
        return [dict(r) for r in self.conn.execute(q, p).fetchall()]

    def get_whats_next(self, limit=20):
        rows = self.conn.execute(
            "SELECT * FROM tasks WHERE status < 5 ORDER BY priority ASC, effort ASC LIMIT ?",
            (limit,)
        ).fetchall()
        return [dict(r) for r in rows]

    def add_comment(self, task_id, content, author='You'):
        now = self._now()
        self.conn.execute(
            "INSERT INTO comments (task_id,content,author,created_at) VALUES (?,?,?,?)",
            (task_id, content, author, now)
        )
        self.conn.commit()
        task = self.get_task(task_id)
        self._audit(task_id, task['title'] if task else '', 'comment',
                    new_value=content[:100])

    def get_comments(self, task_id):
        rows = self.conn.execute(
            "SELECT * FROM comments WHERE task_id=? ORDER BY created_at ASC", (task_id,)
        ).fetchall()
        return [dict(r) for r in rows]

    def get_audit(self, task_id=None, limit=300):
        if task_id:
            rows = self.conn.execute(
                "SELECT * FROM audit_log WHERE task_id=? ORDER BY timestamp DESC LIMIT ?",
                (task_id, limit)
            ).fetchall()
        else:
            rows = self.conn.execute(
                "SELECT * FROM audit_log ORDER BY timestamp DESC LIMIT ?", (limit,)
            ).fetchall()
        return [dict(r) for r in rows]

    def bulk_import(self, records):
        result = self.apply_records(records)
        return result["inserted"]

    def apply_records(self, records):
        summary = {"inserted": 0, "updated": 0, "deleted": 0, "errors": []}
        for r in records:
            try:
                op = str(r.get('insert_type') or r.get('operation') or 'insert').strip().lower()
                if op not in {'insert', 'update', 'delete'}:
                    raise ValueError(f"Unsupported insert_type '{op}'")

                if op == 'insert':
                    self.add_task(
                        title=r.get('title', 'Untitled'),
                        description=r.get('description', ''),
                        status=int(r.get('status') or 1),
                        priority=int(r.get('priority') or 5),
                        effort=int(r.get('effort') or 0),
                        scope=r.get('scope', ''),
                        phase=r.get('phase', ''),
                    )
                    summary["inserted"] += 1
                    continue

                task_id = int(r.get('id') or 0)
                if task_id <= 0:
                    raise ValueError(f"id is required for {op}")

                if op == 'delete':
                    if self.delete_task(task_id):
                        summary["deleted"] += 1
                    continue

                fields = {}
                for field in ('title', 'description', 'status', 'priority', 'effort', 'scope', 'phase'):
                    value = r.get(field)
                    if value not in (None, ''):
                        fields[field] = value
                if self.update_task(task_id, **fields):
                    summary["updated"] += 1
            except Exception as e:
                summary["errors"].append({"error": str(e), "record": dict(r)})
        return summary

    def stats(self):
        rows = self.conn.execute(
            "SELECT status, COUNT(*) as c FROM tasks GROUP BY status"
        ).fetchall()
        total = self.conn.execute("SELECT COUNT(*) FROM tasks").fetchone()[0]
        return {r['status']: r['c'] for r in rows}, total


db = None

# ─── STYLESHEET ──────────────────────────────────────────────────────────────

QSS = """
QWidget { font-family: 'JetBrains Mono', 'Fira Code', 'Consolas', monospace; }

QMainWindow, QDialog { background: #0F1117; }

/* Tabs */
QTabWidget::pane { border: none; background: #0F1117; }
QTabBar { background: #0F1117; }
QTabBar::tab {
    background: #1A1D27; color: #64748B;
    padding: 10px 22px; border: none;
    font-weight: 700; font-size: 11px; letter-spacing: 1.2px;
    text-transform: uppercase;
}
QTabBar::tab:selected { background: #1E2235; color: #E2E8F0; border-bottom: 2px solid #6366F1; }
QTabBar::tab:hover:!selected { background: #1A1D27; color: #94A3B8; }

/* Buttons */
QPushButton {
    background: #6366F1; color: white; border: none;
    padding: 8px 18px; border-radius: 5px;
    font-weight: 700; font-size: 11px; letter-spacing: 0.8px;
}
QPushButton:hover { background: #4F46E5; }
QPushButton:pressed { background: #4338CA; }
QPushButton[secondary="true"] {
    background: #1E2235; color: #94A3B8; border: 1px solid #2A2F45;
}
QPushButton[secondary="true"]:hover { background: #252A3D; color: #E2E8F0; }
QPushButton[danger="true"] {
    background: #1A1D27; color: #EF4444; border: 1px solid #3D1515;
}
QPushButton[danger="true"]:hover { background: #2D1212; }

/* Inputs */
QLineEdit, QTextEdit, QSpinBox, QComboBox {
    background: #1A1D27; border: 1px solid #2A2F45;
    border-radius: 5px; padding: 7px 10px;
    font-size: 12px; color: #E2E8F0;
    selection-background-color: #6366F1;
}
QLineEdit:focus, QTextEdit:focus, QSpinBox:focus { border-color: #6366F1; }
QLineEdit::placeholder { color: #3D4466; }

QComboBox::drop-down { border: none; width: 22px; }
QComboBox::down-arrow { width: 10px; height: 10px; }
QComboBox QAbstractItemView {
    background: #1E2235; border: 1px solid #2A2F45;
    selection-background-color: #6366F1; color: #E2E8F0;
}

QSpinBox::up-button, QSpinBox::down-button { background: #2A2F45; border: none; }
QSpinBox::up-arrow, QSpinBox::down-arrow { color: #64748B; }

/* Table */
QTableWidget {
    background: #0F1117; border: none;
    gridline-color: #1A1D27;
    font-size: 12px; color: #CBD5E1;
    selection-background-color: #1E2235;
    selection-color: #E2E8F0;
    alternate-background-color: #121520;
}
QTableWidget::item { padding: 2px 8px; border-bottom: 1px solid #1A1D27; }
QTableWidget::item:selected { background: #1E2235; color: #E2E8F0; }
QHeaderView::section {
    background: #0F1117; color: #3D4466;
    font-weight: 800; font-size: 10px; letter-spacing: 1.5px;
    padding: 10px 8px; border: none; border-bottom: 1px solid #1E2235;
    text-transform: uppercase;
}
QHeaderView::section:hover { color: #6366F1; }

/* Scroll */
QScrollBar:vertical {
    background: #0F1117; width: 6px; border: none;
}
QScrollBar::handle:vertical { background: #2A2F45; border-radius: 3px; min-height: 20px; }
QScrollBar::handle:vertical:hover { background: #3D4466; }
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }
QScrollBar:horizontal {
    background: #0F1117; height: 6px; border: none;
}
QScrollBar::handle:horizontal { background: #2A2F45; border-radius: 3px; }
QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal { width: 0; }

/* Scroll area */
QScrollArea { border: none; background: transparent; }

/* Group boxes */
QGroupBox {
    color: #3D4466; font-weight: 800; font-size: 10px; letter-spacing: 1.2px;
    border: 1px solid #1E2235; border-radius: 6px;
    margin-top: 14px; padding-top: 8px;
}
QGroupBox::title {
    subcontrol-origin: margin; left: 12px;
    padding: 0 6px; color: #3D4466;
    font-size: 10px; text-transform: uppercase; letter-spacing: 1.2px;
}

/* Labels */
QLabel { color: #94A3B8; }

/* Checkboxes */
QCheckBox { color: #94A3B8; font-size: 12px; spacing: 7px; }
QCheckBox::indicator {
    width: 15px; height: 15px;
    border: 1.5px solid #2A2F45; border-radius: 3px; background: #1A1D27;
}
QCheckBox::indicator:checked {
    background: #6366F1; border-color: #6366F1;
    image: none;
}
QCheckBox::indicator:hover { border-color: #6366F1; }

/* Status bar */
QStatusBar { background: #0A0C14; color: #3D4466; font-size: 10px; border-top: 1px solid #1A1D27; }

/* Splitter */
QSplitter::handle { background: #1A1D27; }
QSplitter::handle:horizontal { width: 1px; }

/* List widget */
QListWidget {
    background: #0F1117; border: 1px solid #1E2235;
    border-radius: 6px; color: #CBD5E1;
}
QListWidget::item { padding: 8px 10px; border-radius: 4px; }
QListWidget::item:hover { background: #1A1D27; }
QListWidget::item:selected { background: #1E2235; color: #E2E8F0; }

/* Menu */
QMenu { background: #1A1D27; border: 1px solid #2A2F45; color: #CBD5E1; padding: 4px; }
QMenu::item { padding: 7px 18px; border-radius: 3px; }
QMenu::item:selected { background: #6366F1; color: white; }

/* Message box */
QMessageBox { background: #1A1D27; }
QMessageBox QLabel { color: #CBD5E1; }
"""

# ─── BADGE HELPERS ───────────────────────────────────────────────────────────

def make_badge_item(text, fg, bg):
    item = QTableWidgetItem(text)
    item.setForeground(QColor(fg))
    item.setBackground(QColor(bg))
    item.setTextAlignment(Qt.AlignCenter)
    item.setFlags(item.flags() & ~Qt.ItemIsEditable)
    return item

def scope_tag_html(scope_str):
    parts = [s.strip() for s in scope_str.split(',') if s.strip()]
    colors = {
        'Backend':  ('#818CF8', '#1E1F3B'),
        'Frontend': ('#34D399', '#0D2620'),
        'Agent':    ('#F472B6', '#2D1225'),
        'DB':       ('#FBBF24', '#2A200A'),
        'Compiler': ('#60A5FA', '#0D1F35'),
        'All':      ('#A78BFA', '#1E1535'),
    }
    tags = []
    for p in parts:
        c, bg = colors.get(p, ('#94A3B8', '#1A1D27'))
        tags.append(f'<span style="background:{bg};color:{c};padding:1px 6px;border-radius:3px;font-size:10px;font-weight:700;">{p}</span>')
    return ' '.join(tags)

# ─── ADD / EDIT DIALOG ───────────────────────────────────────────────────────

class TaskDialog(QDialog):
    def __init__(self, parent=None, task=None):
        super().__init__(parent)
        self.task = task
        title = "Edit Task" if task else "New Task"
        self.setWindowTitle(title)
        self.setMinimumWidth(560)
        self.setMinimumHeight(600)
        self.setStyleSheet(QSS + "QDialog{background:#0F1117;}")
        self._build()
        if task:
            self._populate(task)

    def _build(self):
        layout = QVBoxLayout(self)
        layout.setSpacing(14)
        layout.setContentsMargins(24, 24, 24, 24)

        # Title
        lbl = QLabel("TITLE")
        lbl.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        layout.addWidget(lbl)
        self.title_edit = QLineEdit()
        self.title_edit.setPlaceholderText("Task title...")
        layout.addWidget(self.title_edit)

        # Row: Status, Priority, Effort
        row = QHBoxLayout()
        row.setSpacing(12)

        col1 = QVBoxLayout()
        l = QLabel("STATUS")
        l.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        col1.addWidget(l)
        self.status_combo = QComboBox()
        for k, v in STATUS_CONFIG.items():
            self.status_combo.addItem(f"{k} — {v['name']}", k)
        col1.addWidget(self.status_combo)
        row.addLayout(col1)

        col2 = QVBoxLayout()
        l2 = QLabel("PRIORITY (1=highest)")
        l2.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        col2.addWidget(l2)
        self.priority_spin = QSpinBox()
        self.priority_spin.setRange(1, 10)
        self.priority_spin.setValue(5)
        self.priority_spin.setPrefix("P")
        col2.addWidget(self.priority_spin)
        row.addLayout(col2)

        col3 = QVBoxLayout()
        l3 = QLabel("EFFORT (pts)")
        l3.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        col3.addWidget(l3)
        self.effort_spin = QSpinBox()
        self.effort_spin.setRange(0, 9999)
        self.effort_spin.setSuffix(" pts")
        col3.addWidget(self.effort_spin)
        row.addLayout(col3)

        layout.addLayout(row)

        # Phase
        l4 = QLabel("PHASE")
        l4.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        layout.addWidget(l4)
        self.phase_edit = QLineEdit()
        self.phase_edit.setPlaceholderText("e.g. Phase 1 — Critical Fixes")
        layout.addWidget(self.phase_edit)

        # Scope
        scope_group = QGroupBox("SCOPE")
        scope_grid = QGridLayout(scope_group)
        scope_grid.setSpacing(8)
        self.scope_checks = {}
        for i, s in enumerate(SCOPE_OPTIONS):
            cb = QCheckBox(s)
            self.scope_checks[s] = cb
            scope_grid.addWidget(cb, i // 3, i % 3)
        layout.addWidget(scope_group)

        # Description
        l5 = QLabel("DESCRIPTION / NOTES")
        l5.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        layout.addWidget(l5)
        self.desc_edit = QTextEdit()
        self.desc_edit.setPlaceholderText("Additional details, context, links...")
        self.desc_edit.setMinimumHeight(100)
        self.desc_edit.setMaximumHeight(140)
        layout.addWidget(self.desc_edit)

        layout.addStretch()

        # Buttons
        btn_row = QHBoxLayout()
        cancel_btn = QPushButton("Cancel")
        cancel_btn.setProperty("secondary", True)
        cancel_btn.setStyleSheet("background:#1E2235;color:#64748B;border:1px solid #2A2F45;")
        cancel_btn.clicked.connect(self.reject)
        save_btn = QPushButton("Save Task" if self.task else "Create Task")
        save_btn.clicked.connect(self.accept)
        btn_row.addWidget(cancel_btn)
        btn_row.addWidget(save_btn)
        layout.addLayout(btn_row)

    def _populate(self, task):
        self.title_edit.setText(task.get('title', ''))
        idx = self.status_combo.findData(task.get('status', 1))
        self.status_combo.setCurrentIndex(max(idx, 0))
        self.priority_spin.setValue(task.get('priority', 5))
        self.effort_spin.setValue(task.get('effort', 0))
        self.phase_edit.setText(task.get('phase', ''))
        self.desc_edit.setPlainText(task.get('description', ''))
        scopes = (task.get('scope') or '').split(',')
        for s, cb in self.scope_checks.items():
            cb.setChecked(s.strip() in scopes)

    def get_data(self):
        scope = ','.join(s for s, cb in self.scope_checks.items() if cb.isChecked())
        return dict(
            title=self.title_edit.text().strip(),
            description=self.desc_edit.toPlainText().strip(),
            status=self.status_combo.currentData(),
            priority=self.priority_spin.value(),
            effort=self.effort_spin.value(),
            scope=scope,
            phase=self.phase_edit.text().strip(),
        )

# ─── DETAIL PANEL ────────────────────────────────────────────────────────────

class DetailPanel(QWidget):
    changed = pyqtSignal()

    def __init__(self):
        super().__init__()
        self.task_id = None
        self.setMinimumWidth(330)
        self.setMaximumWidth(400)
        self.setStyleSheet("background:#0C0E18;")
        self._build()

    def _build(self):
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)

        # Header bar
        hdr = QWidget()
        hdr.setFixedHeight(44)
        hdr.setStyleSheet("background:#090B14;border-bottom:1px solid #1A1D27;")
        hdr_row = QHBoxLayout(hdr)
        hdr_row.setContentsMargins(14, 0, 14, 0)
        self.panel_title = QLabel("Task Detail")
        self.panel_title.setStyleSheet("color:#3D4466;font-size:10px;font-weight:800;letter-spacing:1.5px;")
        hdr_row.addWidget(self.panel_title)
        hdr_row.addStretch()
        outer.addWidget(hdr)

        # Scroll
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        scroll.setStyleSheet("QScrollArea{border:none;background:#0C0E18;}")

        self.inner_w = QWidget()
        self.inner_w.setStyleSheet("background:#0C0E18;")
        self.vbox = QVBoxLayout(self.inner_w)
        self.vbox.setContentsMargins(14, 14, 14, 14)
        self.vbox.setSpacing(12)

        # Title edit
        self._lbl("TITLE")
        self.t_title = QLineEdit()
        self.t_title.setStyleSheet(
            "background:#131623;border:none;border-bottom:1px solid #2A2F45;"
            "border-radius:0;padding:6px 2px;font-size:14px;color:#E2E8F0;font-weight:700;")
        self.vbox.addWidget(self.t_title)

        # Phase
        self._lbl("PHASE")
        self.t_phase = QLineEdit()
        self.t_phase.setPlaceholderText("Phase...")
        self.vbox.addWidget(self.t_phase)

        # Status / Priority / Effort row
        row = QHBoxLayout()
        row.setSpacing(8)

        c1 = QVBoxLayout(); c1.setSpacing(4)
        self._mini_lbl("STATUS", c1)
        self.t_status = QComboBox()
        for k, v in STATUS_CONFIG.items():
            self.t_status.addItem(f"{k} — {v['name']}", k)
        c1.addWidget(self.t_status)
        row.addLayout(c1)

        c2 = QVBoxLayout(); c2.setSpacing(4)
        self._mini_lbl("PRIORITY", c2)
        self.t_priority = QSpinBox()
        self.t_priority.setRange(1, 10)
        self.t_priority.setPrefix("P")
        c2.addWidget(self.t_priority)
        row.addLayout(c2)

        c3 = QVBoxLayout(); c3.setSpacing(4)
        self._mini_lbl("EFFORT", c3)
        self.t_effort = QSpinBox()
        self.t_effort.setRange(0, 9999)
        self.t_effort.setSuffix("pt")
        c3.addWidget(self.t_effort)
        row.addLayout(c3)

        self.vbox.addLayout(row)

        # Scope
        scope_grp = QGroupBox("SCOPE")
        sg = QGridLayout(scope_grp)
        sg.setSpacing(6)
        sg.setContentsMargins(10, 12, 10, 10)
        self.t_scope = {}
        for i, s in enumerate(SCOPE_OPTIONS):
            cb = QCheckBox(s)
            self.t_scope[s] = cb
            sg.addWidget(cb, i // 3, i % 3)
        self.vbox.addWidget(scope_grp)

        # Description
        self._lbl("DESCRIPTION")
        self.t_desc = QTextEdit()
        self.t_desc.setPlaceholderText("Notes, context...")
        self.t_desc.setMinimumHeight(80)
        self.t_desc.setMaximumHeight(120)
        self.vbox.addWidget(self.t_desc)

        # Save
        save_btn = QPushButton("▶  Save Changes")
        save_btn.clicked.connect(self._save)
        self.vbox.addWidget(save_btn)

        # Comments
        c_grp = QGroupBox("COMMENTS")
        c_layout = QVBoxLayout(c_grp)
        c_layout.setContentsMargins(10, 14, 10, 10)
        c_layout.setSpacing(8)
        self.comments_container = QVBoxLayout()
        self.comments_container.setSpacing(6)
        c_layout.addLayout(self.comments_container)
        comment_row = QHBoxLayout()
        self.t_comment = QLineEdit()
        self.t_comment.setPlaceholderText("Add comment...")
        self.t_comment.returnPressed.connect(self._add_comment)
        add_c = QPushButton("+")
        add_c.setFixedWidth(34)
        add_c.setFixedHeight(32)
        add_c.clicked.connect(self._add_comment)
        comment_row.addWidget(self.t_comment)
        comment_row.addWidget(add_c)
        c_layout.addLayout(comment_row)
        self.vbox.addWidget(c_grp)

        # Audit log
        a_grp = QGroupBox("CHANGE LOG")
        a_layout = QVBoxLayout(a_grp)
        a_layout.setContentsMargins(10, 14, 10, 10)
        self.audit_container = QVBoxLayout()
        self.audit_container.setSpacing(2)
        a_layout.addLayout(self.audit_container)
        self.vbox.addWidget(a_grp)

        # Timestamps
        self.ts_lbl = QLabel()
        self.ts_lbl.setStyleSheet("color:#2A2F45;font-size:10px;")
        self.ts_lbl.setWordWrap(True)
        self.vbox.addWidget(self.ts_lbl)

        # Delete
        del_btn = QPushButton("Delete Task")
        del_btn.setProperty("danger", True)
        del_btn.clicked.connect(self._delete)
        self.vbox.addWidget(del_btn)

        self.vbox.addStretch()

        scroll.setWidget(self.inner_w)
        outer.addWidget(scroll)

    def _lbl(self, text):
        l = QLabel(text)
        l.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;margin-top:4px;")
        self.vbox.addWidget(l)

    def _mini_lbl(self, text, layout):
        l = QLabel(text)
        l.setStyleSheet("font-size:9px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        layout.addWidget(l)

    def load(self, task_id):
        self.task_id = task_id
        task = db.get_task(task_id)
        if not task:
            return
        self.t_title.setText(task['title'])
        self.t_phase.setText(task.get('phase') or '')
        idx = self.t_status.findData(task['status'])
        self.t_status.setCurrentIndex(max(idx, 0))
        self.t_priority.setValue(task['priority'])
        self.t_effort.setValue(task.get('effort') or 0)
        self.t_desc.setPlainText(task.get('description') or '')
        scopes = (task.get('scope') or '').split(',')
        for s, cb in self.t_scope.items():
            cb.setChecked(s.strip() in scopes)
        created = ts_fmt(task['created_at'])
        updated = ts_fmt(task['updated_at'])
        self.ts_lbl.setText(f"Created {created}  ·  Updated {updated}")
        self._load_comments()
        self._load_audit()

    def _load_comments(self):
        self._clear(self.comments_container)
        comments = db.get_comments(self.task_id)
        if not comments:
            l = QLabel("No comments yet")
            l.setStyleSheet("color:#2A2F45;font-style:italic;font-size:11px;padding:4px;")
            self.comments_container.addWidget(l)
        for c in comments:
            w = QFrame()
            w.setStyleSheet("background:#131623;border-radius:5px;border:1px solid #1A1D27;")
            fl = QVBoxLayout(w)
            fl.setContentsMargins(10, 8, 10, 8)
            fl.setSpacing(3)
            top = QHBoxLayout()
            author = QLabel(c['author'])
            author.setStyleSheet("font-weight:800;color:#6366F1;font-size:11px;")
            ts = QLabel(ts_fmt(c['created_at']))
            ts.setStyleSheet("color:#2A2F45;font-size:10px;")
            top.addWidget(author); top.addStretch(); top.addWidget(ts)
            fl.addLayout(top)
            content = QLabel(c['content'])
            content.setWordWrap(True)
            content.setStyleSheet("color:#94A3B8;font-size:12px;")
            fl.addWidget(content)
            self.comments_container.addWidget(w)

    def _load_audit(self):
        self._clear(self.audit_container)
        logs = db.get_audit(task_id=self.task_id, limit=30)
        if not logs:
            l = QLabel("No changes yet")
            l.setStyleSheet("color:#2A2F45;font-style:italic;font-size:11px;padding:4px;")
            self.audit_container.addWidget(l)
        for log in logs:
            text = self._audit_text(log)
            row = QHBoxLayout()
            row.setSpacing(8)
            dot = QLabel("›")
            dot.setStyleSheet("color:#6366F1;font-size:14px;")
            dot.setFixedWidth(10)
            lbl = QLabel(f"<span style='color:#3D4466;'>{ts_fmt(log['timestamp'])}</span>  {text}")
            lbl.setStyleSheet("color:#64748B;font-size:11px;")
            lbl.setWordWrap(True)
            row.addWidget(dot, 0, Qt.AlignTop)
            row.addWidget(lbl, 1)
            w = QWidget()
            w.setLayout(row)
            self.audit_container.addWidget(w)

    def _audit_text(self, log):
        action = log.get('action', '')
        field = log.get('field', '')
        ov, nv = log.get('old_value') or '', log.get('new_value') or ''
        if action == 'created':
            return f"<b>Task created</b> — {nv}"
        if action == 'deleted':
            return "<b>Task deleted</b>"
        if action == 'comment':
            return f"<b>Comment added:</b> {nv[:60]}"
        if action == 'updated' and field:
            field_disp = field.replace('_', ' ').title()
            if ov and nv:
                return f"<b>{field_disp}</b> changed: <span style='color:#EF4444;'>{ov}</span> → <span style='color:#34D399;'>{nv}</span>"
            return f"<b>{field_disp}</b> updated"
        return action

    def _clear(self, layout):
        while layout.count():
            item = layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

    def _save(self):
        if not self.task_id:
            return
        scope = ','.join(s for s, cb in self.t_scope.items() if cb.isChecked())
        db.update_task(self.task_id,
                       title=self.t_title.text().strip(),
                       phase=self.t_phase.text().strip(),
                       status=self.t_status.currentData(),
                       priority=self.t_priority.value(),
                       effort=self.t_effort.value(),
                       scope=scope,
                       description=self.t_desc.toPlainText().strip())
        self._load_audit()
        self.changed.emit()

    def _add_comment(self):
        if not self.task_id:
            return
        text = self.t_comment.text().strip()
        if not text:
            return
        db.add_comment(self.task_id, text)
        self.t_comment.clear()
        self._load_comments()
        self._load_audit()

    def _delete(self):
        if not self.task_id:
            return
        reply = QMessageBox.question(self, "Delete", "Delete this task permanently?",
                                     QMessageBox.Yes | QMessageBox.No)
        if reply == QMessageBox.Yes:
            db.delete_task(self.task_id)
            self.task_id = None
            self.changed.emit()
            self.hide()

# ─── TASK TABLE VIEW ─────────────────────────────────────────────────────────

COL_HEADERS = ["#", "TITLE", "STATUS", "P", "EFFORT", "SCOPE", "PHASE", "UPDATED"]
COL_WIDTHS   = [ 40,   260,     110,   50,    60,      160,     140,     110]

class TaskTable(QWidget):
    task_selected = pyqtSignal(int)
    data_changed  = pyqtSignal()

    def __init__(self):
        super().__init__()
        self.sort_col = 'priority'
        self.sort_asc = True
        self.status_filter = 0
        self.scope_filter  = ''
        self.search_text   = ''
        self._rows = []
        self._build()

    def _build(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # Filter bar
        filter_bar = QWidget()
        filter_bar.setFixedHeight(52)
        filter_bar.setStyleSheet("background:#090B14;border-bottom:1px solid #1A1D27;")
        fb = QHBoxLayout(filter_bar)
        fb.setContentsMargins(14, 0, 14, 0)
        fb.setSpacing(10)

        self.search = QLineEdit()
        self.search.setPlaceholderText("Search tasks...")
        self.search.setMaximumWidth(240)
        self.search.textChanged.connect(self._on_filter_change)
        fb.addWidget(self.search)

        self.status_cb = QComboBox()
        self.status_cb.setMaximumWidth(160)
        self.status_cb.addItem("All Statuses", 0)
        for k, v in STATUS_CONFIG.items():
            self.status_cb.addItem(f"{v['dot']} {v['name']}", k)
        self.status_cb.currentIndexChanged.connect(self._on_filter_change)
        fb.addWidget(self.status_cb)

        self.scope_cb = QComboBox()
        self.scope_cb.setMaximumWidth(130)
        self.scope_cb.addItem("All Scopes", '')
        for s in SCOPE_OPTIONS:
            self.scope_cb.addItem(s, s)
        self.scope_cb.currentIndexChanged.connect(self._on_filter_change)
        fb.addWidget(self.scope_cb)

        fb.addStretch()

        # Sort
        sort_lbl = QLabel("SORT:")
        sort_lbl.setStyleSheet("color:#3D4466;font-size:10px;font-weight:800;letter-spacing:1px;")
        fb.addWidget(sort_lbl)
        self.sort_cb = QComboBox()
        self.sort_cb.setMaximumWidth(140)
        for label, val in [("Priority", "priority"), ("Status", "status"),
                            ("Title", "title"), ("Effort", "effort"), ("Updated", "updated_at")]:
            self.sort_cb.addItem(label, val)
        self.sort_cb.currentIndexChanged.connect(self._on_sort_change)
        fb.addWidget(self.sort_cb)

        self.sort_dir = QPushButton("↑ ASC")
        self.sort_dir.setProperty("secondary", True)
        self.sort_dir.setMaximumWidth(70)
        self.sort_dir.setStyleSheet("background:#131623;color:#64748B;border:1px solid #1A1D27;font-size:10px;padding:5px 8px;")
        self.sort_dir.clicked.connect(self._toggle_sort_dir)
        fb.addWidget(self.sort_dir)

        layout.addWidget(filter_bar)

        # Table
        self.table = QTableWidget()
        self.table.setColumnCount(len(COL_HEADERS))
        self.table.setHorizontalHeaderLabels(COL_HEADERS)
        self.table.setAlternatingRowColors(True)
        self.table.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.table.setSelectionMode(QAbstractItemView.SingleSelection)
        self.table.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.table.verticalHeader().setVisible(False)
        self.table.setShowGrid(False)
        self.table.horizontalHeader().setHighlightSections(False)
        self.table.horizontalHeader().setSortIndicatorShown(False)
        self.table.setContextMenuPolicy(Qt.CustomContextMenu)
        self.table.customContextMenuRequested.connect(self._ctx_menu)
        self.table.cellDoubleClicked.connect(self._on_double_click)
        self.table.currentCellChanged.connect(self._on_select)
        self.table.setFocusPolicy(Qt.StrongFocus)

        for i, w in enumerate(COL_WIDTHS):
            self.table.setColumnWidth(i, w)
        self.table.horizontalHeader().setSectionResizeMode(1, QHeaderView.Stretch)

        layout.addWidget(self.table)
        self.load()

    def load(self):
        self._rows = db.get_tasks(
            status_filter=self.status_filter,
            scope_filter=self.scope_filter,
            search=self.search_text,
            sort_col=self.sort_col,
            sort_asc=self.sort_asc,
        )
        self._render()

    def _render(self):
        self.table.blockSignals(True)
        self.table.setRowCount(len(self._rows))
        for r, task in enumerate(self._rows):
            self.table.setRowHeight(r, 36)

            # ID
            id_item = QTableWidgetItem(str(task['id']))
            id_item.setTextAlignment(Qt.AlignCenter)
            id_item.setForeground(QColor("#2A2F45"))
            id_item.setFlags(id_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 0, id_item)

            # Title
            title_item = QTableWidgetItem(task['title'])
            title_item.setForeground(QColor("#E2E8F0"))
            title_item.setFlags(title_item.flags() & ~Qt.ItemIsEditable)
            if task['status'] == 5:
                title_item.setForeground(QColor("#3D4466"))
            self.table.setItem(r, 1, title_item)

            # Status badge
            sc = STATUS_CONFIG.get(task['status'], STATUS_CONFIG[1])
            st_item = make_badge_item(f"{sc['dot']} {sc['short']}", sc['color'], sc['bg'] + "22")
            self.table.setItem(r, 2, st_item)

            # Priority
            pc, pb = priority_style(task['priority'])
            p_item = make_badge_item(f"P{task['priority']}", pc, pb + "22")
            self.table.setItem(r, 3, p_item)

            # Effort
            effort_item = QTableWidgetItem(f"{task.get('effort',0)}pt" if task.get('effort') else "—")
            effort_item.setTextAlignment(Qt.AlignCenter)
            effort_item.setForeground(QColor("#3D4466"))
            effort_item.setFlags(effort_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 4, effort_item)

            # Scope
            scope_str = task.get('scope') or ''
            scope_item = QTableWidgetItem(scope_str.replace(',', ' · '))
            scope_item.setForeground(QColor("#64748B"))
            scope_item.setFlags(scope_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 5, scope_item)

            # Phase
            phase_item = QTableWidgetItem(task.get('phase') or '')
            phase_item.setForeground(QColor("#3D4466"))
            phase_item.setFlags(phase_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 6, phase_item)

            # Updated
            up_item = QTableWidgetItem(ts_fmt(task['updated_at']))
            up_item.setTextAlignment(Qt.AlignCenter)
            up_item.setForeground(QColor("#2A2F45"))
            up_item.setFlags(up_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 7, up_item)

        self.table.blockSignals(False)
        self.data_changed.emit()

    def _on_filter_change(self):
        self.search_text  = self.search.text()
        self.status_filter = self.status_cb.currentData()
        self.scope_filter  = self.scope_cb.currentData()
        self.load()

    def _on_sort_change(self):
        self.sort_col = self.sort_cb.currentData()
        self.load()

    def _toggle_sort_dir(self):
        self.sort_asc = not self.sort_asc
        self.sort_dir.setText("↑ ASC" if self.sort_asc else "↓ DESC")
        self.load()

    def _on_select(self, row, col, prev_row, prev_col):
        if row >= 0 and row < len(self._rows):
            self.task_selected.emit(self._rows[row]['id'])

    def _on_double_click(self, row, col):
        if row >= 0 and row < len(self._rows):
            self.task_selected.emit(self._rows[row]['id'])

    def _ctx_menu(self, pos):
        row = self.table.rowAt(pos.y())
        if row < 0 or row >= len(self._rows):
            return
        task = self._rows[row]
        menu = QMenu(self)
        edit_a  = menu.addAction("✏  Edit")
        menu.addSeparator()
        done_a  = menu.addAction("✓  Mark Done")
        prog_a  = menu.addAction("◔  Mark In Progress")
        menu.addSeparator()
        del_a   = menu.addAction("✕  Delete")
        action = menu.exec_(self.table.mapToGlobal(pos))
        if action == edit_a:
            self._quick_edit(task)
        elif action == done_a:
            db.update_task(task['id'], status=5)
            self.load()
        elif action == prog_a:
            db.update_task(task['id'], status=2)
            self.load()
        elif action == del_a:
            if QMessageBox.question(self, "Delete", f"Delete '{task['title']}'?",
                                    QMessageBox.Yes | QMessageBox.No) == QMessageBox.Yes:
                db.delete_task(task['id'])
                self.load()

    def _quick_edit(self, task):
        dlg = TaskDialog(self, task)
        if dlg.exec_() == QDialog.Accepted:
            data = dlg.get_data()
            if data['title']:
                db.update_task(task['id'], **data)
                self.load()

    def get_selected_id(self):
        row = self.table.currentRow()
        if row >= 0 and row < len(self._rows):
            return self._rows[row]['id']
        return None

# ─── WHAT'S NEXT VIEW ────────────────────────────────────────────────────────

class WhatsNextView(QWidget):
    def __init__(self):
        super().__init__()
        self._build()

    def _build(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # Header
        hdr = QWidget()
        hdr.setFixedHeight(52)
        hdr.setStyleSheet("background:#090B14;border-bottom:1px solid #1A1D27;")
        hrow = QHBoxLayout(hdr)
        hrow.setContentsMargins(18, 0, 18, 0)
        lbl = QLabel("▶  WHAT'S NEXT")
        lbl.setStyleSheet("color:#6366F1;font-size:11px;font-weight:800;letter-spacing:2px;")
        sub = QLabel("— ordered by priority, excluding Done")
        sub.setStyleSheet("color:#3D4466;font-size:11px;margin-left:8px;")
        hrow.addWidget(lbl)
        hrow.addWidget(sub)
        hrow.addStretch()
        layout.addWidget(hdr)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setStyleSheet("QScrollArea{border:none;background:#0F1117;}")

        self.cards_widget = QWidget()
        self.cards_widget.setStyleSheet("background:#0F1117;")
        self.cards_layout = QVBoxLayout(self.cards_widget)
        self.cards_layout.setContentsMargins(18, 18, 18, 18)
        self.cards_layout.setSpacing(8)

        scroll.setWidget(self.cards_widget)
        layout.addWidget(scroll)
        self.load()

    def load(self):
        while self.cards_layout.count():
            item = self.cards_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()

        tasks = db.get_whats_next(limit=25)

        if not tasks:
            empty = QLabel("🎉  All tasks are done!")
            empty.setStyleSheet("color:#3D4466;font-size:14px;padding:40px;")
            empty.setAlignment(Qt.AlignCenter)
            self.cards_layout.addWidget(empty)
            return

        for i, task in enumerate(tasks):
            card = self._make_card(task, i + 1)
            self.cards_layout.addWidget(card)

        self.cards_layout.addStretch()

    def _make_card(self, task, rank):
        frame = QFrame()
        sc = STATUS_CONFIG.get(task['status'], STATUS_CONFIG[1])
        pc, pb = priority_style(task['priority'])
        border_color = pc

        frame.setStyleSheet(f"""
            QFrame {{
                background: #131623;
                border: 1px solid #1E2235;
                border-left: 3px solid {border_color};
                border-radius: 6px;
            }}
            QFrame:hover {{ background: #161928; border-color: {border_color}; }}
        """)

        fl = QHBoxLayout(frame)
        fl.setContentsMargins(16, 12, 16, 12)
        fl.setSpacing(14)

        # Rank
        rank_lbl = QLabel(f"#{rank}")
        rank_lbl.setFixedWidth(28)
        rank_lbl.setStyleSheet(f"color:{pc};font-size:11px;font-weight:800;")
        fl.addWidget(rank_lbl)

        # Main content
        content = QVBoxLayout()
        content.setSpacing(4)

        title_lbl = QLabel(task['title'])
        title_lbl.setStyleSheet("color:#E2E8F0;font-size:13px;font-weight:700;")
        content.addWidget(title_lbl)

        meta_row = QHBoxLayout()
        meta_row.setSpacing(10)

        if task.get('phase'):
            phase_lbl = QLabel(task['phase'])
            phase_lbl.setStyleSheet("color:#3D4466;font-size:10px;")
            meta_row.addWidget(phase_lbl)

        scope_str = task.get('scope', '')
        if scope_str:
            s_lbl = QLabel(scope_str.replace(',', ' · '))
            s_lbl.setStyleSheet("color:#64748B;font-size:10px;")
            meta_row.addWidget(s_lbl)

        meta_row.addStretch()
        content.addLayout(meta_row)
        fl.addLayout(content, 1)

        # Right side badges
        right = QVBoxLayout()
        right.setSpacing(5)
        right.setAlignment(Qt.AlignCenter)

        p_lbl = QLabel(f"P{task['priority']}")
        p_lbl.setStyleSheet(f"color:{pc};background:{pb}22;border-radius:3px;"
                            f"padding:2px 8px;font-size:11px;font-weight:800;")
        p_lbl.setAlignment(Qt.AlignCenter)
        right.addWidget(p_lbl)

        st_lbl = QLabel(f"{sc['dot']} {sc['short']}")
        st_lbl.setStyleSheet(f"color:{sc['color']};background:{sc['bg']}22;border-radius:3px;"
                             f"padding:2px 8px;font-size:10px;font-weight:700;")
        st_lbl.setAlignment(Qt.AlignCenter)
        right.addWidget(st_lbl)

        if task.get('effort'):
            e_lbl = QLabel(f"{task['effort']}pt")
            e_lbl.setStyleSheet("color:#3D4466;font-size:10px;")
            e_lbl.setAlignment(Qt.AlignCenter)
            right.addWidget(e_lbl)

        fl.addLayout(right)
        return frame

# ─── AUDIT LOG VIEW ──────────────────────────────────────────────────────────

class AuditLogView(QWidget):
    def __init__(self):
        super().__init__()
        self._build()

    def _build(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        hdr = QWidget()
        hdr.setFixedHeight(52)
        hdr.setStyleSheet("background:#090B14;border-bottom:1px solid #1A1D27;")
        hrow = QHBoxLayout(hdr)
        hrow.setContentsMargins(18, 0, 18, 0)
        lbl = QLabel("⏱  AUDIT LOG")
        lbl.setStyleSheet("color:#6366F1;font-size:11px;font-weight:800;letter-spacing:2px;")
        hrow.addWidget(lbl)
        hrow.addStretch()
        refresh_btn = QPushButton("Refresh")
        refresh_btn.setProperty("secondary", True)
        refresh_btn.setStyleSheet("background:#131623;color:#64748B;border:1px solid #1A1D27;font-size:10px;padding:4px 12px;")
        refresh_btn.clicked.connect(self.load)
        hrow.addWidget(refresh_btn)
        layout.addWidget(hdr)

        self.table = QTableWidget()
        self.table.setColumnCount(6)
        self.table.setHorizontalHeaderLabels(["TIME", "TASK", "ACTION", "FIELD", "BEFORE", "AFTER"])
        self.table.setAlternatingRowColors(True)
        self.table.setSelectionBehavior(QAbstractItemView.SelectRows)
        self.table.setEditTriggers(QAbstractItemView.NoEditTriggers)
        self.table.verticalHeader().setVisible(False)
        self.table.setShowGrid(False)
        self.table.horizontalHeader().setHighlightSections(False)
        self.table.setColumnWidth(0, 130)
        self.table.setColumnWidth(1, 220)
        self.table.setColumnWidth(2, 100)
        self.table.setColumnWidth(3, 90)
        self.table.setColumnWidth(4, 130)
        self.table.horizontalHeader().setSectionResizeMode(5, QHeaderView.Stretch)
        layout.addWidget(self.table)
        self.load()

    def load(self):
        logs = db.get_audit(limit=300)
        self.table.setRowCount(len(logs))
        action_colors = {
            'created': ('#34D399', '#0D2620'),
            'deleted': ('#EF4444', '#2D1212'),
            'updated': ('#60A5FA', '#0D1F35'),
            'comment': ('#FBBF24', '#2A200A'),
        }
        for r, log in enumerate(logs):
            self.table.setRowHeight(r, 30)
            ts_item = QTableWidgetItem(ts_fmt(log['timestamp']))
            ts_item.setForeground(QColor("#3D4466"))
            ts_item.setFlags(ts_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 0, ts_item)

            task_item = QTableWidgetItem(log.get('task_title') or '')
            task_item.setForeground(QColor("#94A3B8"))
            task_item.setFlags(task_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 1, task_item)

            action = log.get('action', '')
            ac, ab = action_colors.get(action, ('#64748B', '#1A1D27'))
            a_item = make_badge_item(action.upper(), ac, ab)
            self.table.setItem(r, 2, a_item)

            field_item = QTableWidgetItem(log.get('field') or '')
            field_item.setForeground(QColor("#3D4466"))
            field_item.setFlags(field_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 3, field_item)

            ov_item = QTableWidgetItem(log.get('old_value') or '')
            ov_item.setForeground(QColor("#EF4444"))
            ov_item.setFlags(ov_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 4, ov_item)

            nv_item = QTableWidgetItem(log.get('new_value') or '')
            nv_item.setForeground(QColor("#34D399"))
            nv_item.setFlags(nv_item.flags() & ~Qt.ItemIsEditable)
            self.table.setItem(r, 5, nv_item)

# ─── BULK IMPORT DIALOG ──────────────────────────────────────────────────────

IMPORT_HELP = """SUPPORTED FORMATS
─────────────────

CSV (comma-separated):
  insert_type,id,title,description,priority,status,effort,scope,phase

JSON (array of objects):
  [
    {
      "insert_type": "insert",
      "title": "My Task",
      "description": "Details here",
      "priority": 1,
      "status": 1,
      "effort": 3,
      "scope": "Backend,Frontend",
      "phase": "Phase 1"
    }
  ]

FIELD REFERENCE
────────────────
insert_type : insert, update, or delete
id       : required for update/delete; ignored for insert
priority : 1 (highest) – 10 (lowest)
status   : 1=Not Started  2=In Progress
           3=Started      4=Kinda Done   5=Done
effort   : integer (story points)
scope    : comma-separated from:
           Backend, Frontend, Agent, DB, Compiler, All"""

class BulkImportDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Bulk Import Tasks")
        self.setMinimumSize(680, 560)
        self.setStyleSheet(QSS + "QDialog{background:#0F1117;}")
        self._build()

    def _build(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 20, 20, 20)
        layout.setSpacing(12)

        top = QHBoxLayout()

        # Left: paste area
        left = QVBoxLayout()
        left.setSpacing(8)
        paste_lbl = QLabel("PASTE CSV OR JSON")
        paste_lbl.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        left.addWidget(paste_lbl)
        self.paste_area = QTextEdit()
        self.paste_area.setPlaceholderText("Paste CSV or JSON here...")
        self.paste_area.setMinimumHeight(300)
        self.paste_area.setStyleSheet("font-family:monospace;font-size:11px;")
        left.addWidget(self.paste_area)

        load_file_btn = QPushButton("Load from File (.csv / .json)")
        load_file_btn.setProperty("secondary", True)
        load_file_btn.setStyleSheet("background:#1A1D27;color:#64748B;border:1px solid #2A2F45;")
        load_file_btn.clicked.connect(self._load_file)
        left.addWidget(load_file_btn)
        top.addLayout(left, 3)

        # Right: help
        right = QVBoxLayout()
        right.setSpacing(8)
        fmt_lbl = QLabel("FORMAT GUIDE")
        fmt_lbl.setStyleSheet("font-size:10px;font-weight:800;letter-spacing:1.2px;color:#3D4466;")
        right.addWidget(fmt_lbl)
        help_text = QTextEdit()
        help_text.setPlainText(IMPORT_HELP)
        help_text.setReadOnly(True)
        help_text.setStyleSheet("font-family:monospace;font-size:10px;color:#3D4466;background:#090B14;border:1px solid #1A1D27;")
        help_text.setMinimumHeight(300)
        right.addWidget(help_text)
        right.addStretch()
        top.addLayout(right, 2)

        layout.addLayout(top)

        self.result_lbl = QLabel("")
        self.result_lbl.setStyleSheet("color:#34D399;font-size:12px;")
        layout.addWidget(self.result_lbl)

        btn_row = QHBoxLayout()
        cancel = QPushButton("Cancel")
        cancel.setProperty("secondary", True)
        cancel.setStyleSheet("background:#1A1D27;color:#64748B;border:1px solid #2A2F45;")
        cancel.clicked.connect(self.reject)
        self.import_btn = QPushButton("▶  Import Tasks")
        self.import_btn.clicked.connect(self._do_import)
        btn_row.addWidget(cancel)
        btn_row.addWidget(self.import_btn)
        layout.addLayout(btn_row)

    def _load_file(self):
        path, _ = QFileDialog.getOpenFileName(self, "Open Import File", "",
                                               "CSV / JSON Files (*.csv *.json)")
        if path:
            with open(path, 'r', encoding='utf-8') as f:
                self.paste_area.setPlainText(f.read())

    def _do_import(self):
        raw = self.paste_area.toPlainText().strip()
        if not raw:
            self.result_lbl.setText("Nothing to import.")
            return
        records = []
        try:
            data = json.loads(raw)
            if isinstance(data, list):
                records = data
            else:
                self.result_lbl.setStyleSheet("color:#EF4444;font-size:12px;")
                self.result_lbl.setText("JSON must be an array of objects.")
                return
        except json.JSONDecodeError:
            try:
                reader = csv.DictReader(io.StringIO(raw))
                records = list(reader)
            except Exception as e:
                self.result_lbl.setStyleSheet("color:#EF4444;font-size:12px;")
                self.result_lbl.setText(f"Parse error: {e}")
                return

        if not records:
            self.result_lbl.setText("No records found.")
            return

        summary = db.apply_records(records)
        self.result_lbl.setStyleSheet("color:#34D399;font-size:12px;")
        self.result_lbl.setText(
            f"Applied {summary['inserted']} inserts, {summary['updated']} updates, "
            f"{summary['deleted']} deletes, {len(summary['errors'])} errors."
        )
        QTimer.singleShot(1500, self.accept)

# ─── MAIN WINDOW ─────────────────────────────────────────────────────────────

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Pipeline Task Manager")
        self.setMinimumSize(1200, 720)
        self.resize(1400, 860)
        self.setStyleSheet(QSS)
        self._build()
        self._update_status()

    def _build(self):
        central = QWidget()
        central.setStyleSheet("background:#0F1117;")
        self.setCentralWidget(central)
        root = QVBoxLayout(central)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        # ── Top toolbar ──
        toolbar = QWidget()
        toolbar.setFixedHeight(56)
        toolbar.setStyleSheet("background:#090B14;border-bottom:1px solid #1A1D27;")
        tb = QHBoxLayout(toolbar)
        tb.setContentsMargins(16, 0, 16, 0)
        tb.setSpacing(10)

        app_lbl = QLabel("◈ TASKMAN")
        app_lbl.setStyleSheet("color:#6366F1;font-size:13px;font-weight:800;letter-spacing:2px;")
        tb.addWidget(app_lbl)

        sep = QFrame()
        sep.setFrameShape(QFrame.VLine)
        sep.setStyleSheet("color:#1E2235;")
        tb.addWidget(sep)

        new_btn = QPushButton("+ New Task")
        new_btn.clicked.connect(self._new_task)
        tb.addWidget(new_btn)

        import_btn = QPushButton("⬆  Bulk Import")
        import_btn.setProperty("secondary", True)
        import_btn.setStyleSheet("background:#1A1D27;color:#64748B;border:1px solid #1E2235;")
        import_btn.clicked.connect(self._bulk_import)
        tb.addWidget(import_btn)

        export_btn = QPushButton("⬇  Export CSV")
        export_btn.setProperty("secondary", True)
        export_btn.setStyleSheet("background:#1A1D27;color:#64748B;border:1px solid #1E2235;")
        export_btn.clicked.connect(self._export_csv)
        tb.addWidget(export_btn)

        tb.addStretch()

        # Stats row
        self.stat_lbl = QLabel()
        self.stat_lbl.setStyleSheet("color:#3D4466;font-size:10px;letter-spacing:0.5px;")
        tb.addWidget(self.stat_lbl)

        root.addWidget(toolbar)

        # ── Tabs + Content ──
        self.tabs = QTabWidget()
        self.tabs.setTabPosition(QTabWidget.North)
        self.tabs.currentChanged.connect(self._on_tab_change)
        root.addWidget(self.tabs)

        # Tasks tab
        tasks_w = QWidget()
        tasks_w.setStyleSheet("background:#0F1117;")
        tasks_layout = QHBoxLayout(tasks_w)
        tasks_layout.setContentsMargins(0, 0, 0, 0)
        tasks_layout.setSpacing(0)

        splitter = QSplitter(Qt.Horizontal)
        splitter.setHandleWidth(1)

        self.task_table = TaskTable()
        self.task_table.task_selected.connect(self._on_task_selected)
        self.task_table.data_changed.connect(self._update_status)
        splitter.addWidget(self.task_table)

        self.detail_panel = DetailPanel()
        self.detail_panel.changed.connect(self._on_detail_changed)
        self.detail_panel.hide()
        splitter.addWidget(self.detail_panel)

        splitter.setStretchFactor(0, 3)
        splitter.setStretchFactor(1, 1)
        tasks_layout.addWidget(splitter)

        self.tabs.addTab(tasks_w, "  Tasks  ")

        # What's Next tab
        self.whats_next = WhatsNextView()
        self.tabs.addTab(self.whats_next, "  What's Next  ")

        # Audit tab
        self.audit_view = AuditLogView()
        self.tabs.addTab(self.audit_view, "  Audit Log  ")

        # Status bar
        self.status_bar = QStatusBar()
        self.setStatusBar(self.status_bar)
        self.status_bar.showMessage("Ready  ·  Ctrl+N: New Task  ·  Double-click to open  ·  Right-click for quick actions")

        # Shortcuts
        from PyQt5.QtWidgets import QShortcut
        from PyQt5.QtGui import QKeySequence
        QShortcut(QKeySequence("Ctrl+N"), self, self._new_task)
        QShortcut(QKeySequence("Ctrl+R"), self, self._refresh_all)

    def _new_task(self):
        dlg = TaskDialog(self)
        if dlg.exec_() == QDialog.Accepted:
            data = dlg.get_data()
            if data['title']:
                db.add_task(**data)
                self.task_table.load()
                self._update_status()

    def _bulk_import(self):
        dlg = BulkImportDialog(self)
        if dlg.exec_() == QDialog.Accepted:
            self.task_table.load()
            self._update_status()

    def _export_csv(self):
        path, _ = QFileDialog.getSaveFileName(self, "Export Tasks", "tasks_export.csv",
                                               "CSV Files (*.csv)")
        if not path:
            return
        tasks = db.get_tasks()
        fieldnames = ['id','title','description','status','priority','effort','scope','phase','created_at','updated_at']
        with open(path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames, extrasaction='ignore')
            writer.writeheader()
            for t in tasks:
                # Translate status to name
                t['status'] = STATUS_CONFIG.get(t['status'], {}).get('name', t['status'])
                writer.writerow(t)
        self.status_bar.showMessage(f"Exported {len(tasks)} tasks to {path}")

    def _on_task_selected(self, task_id):
        self.detail_panel.show()
        self.detail_panel.load(task_id)

    def _on_detail_changed(self):
        self.task_table.load()
        self._update_status()

    def _on_tab_change(self, idx):
        if idx == 1:
            self.whats_next.load()
        elif idx == 2:
            self.audit_view.load()

    def _refresh_all(self):
        self.task_table.load()
        self._update_status()

    def _update_status(self):
        counts, total = db.stats()
        done = counts.get(5, 0)
        in_prog = counts.get(2, 0) + counts.get(3, 0)
        not_started = counts.get(1, 0)
        pct = int((done / total * 100)) if total else 0
        self.stat_lbl.setText(
            f"TOTAL {total}  ·  DONE {done} ({pct}%)  ·  ACTIVE {in_prog}  ·  QUEUED {not_started}"
        )

# ─── CLI API ────────────────────────────────────────────────────────────────

INPUT_FIELDS = ['insert_type', 'id', 'title', 'description', 'status', 'priority', 'effort', 'scope', 'phase']
EXPORT_FIELDS = ['id', 'title', 'description', 'status', 'priority', 'effort', 'scope', 'phase', 'created_at', 'updated_at']

def read_records(path):
    source = Path(path)
    raw = source.read_text(encoding='utf-8')
    if source.suffix.lower() == '.json':
        data = json.loads(raw)
        if not isinstance(data, list):
            raise ValueError("JSON upload must be an array of objects")
        return data
    return list(csv.DictReader(io.StringIO(raw)))

def write_output(payload, fmt='json', output_path=None):
    if fmt == 'csv':
        rows = payload if isinstance(payload, list) else [payload]
        fieldnames = EXPORT_FIELDS
        if rows:
            fieldnames = [field for field in EXPORT_FIELDS if field in rows[0]]
            extras = [field for field in rows[0].keys() if field not in fieldnames]
            fieldnames.extend(extras)
        target = open(output_path, 'w', newline='', encoding='utf-8') if output_path else sys.stdout
        close_target = output_path is not None
        try:
            writer = csv.DictWriter(target, fieldnames=fieldnames, extrasaction='ignore')
            writer.writeheader()
            for row in rows:
                writer.writerow(row)
        finally:
            if close_target:
                target.close()
        return

    text = json.dumps(payload, indent=2, default=str)
    if output_path:
        Path(output_path).write_text(text + "\n", encoding='utf-8')
    else:
        print(text)

def build_cli_parser():
    parser = argparse.ArgumentParser(description="Query and mutate the finance task tracker database.")
    parser.add_argument('--db', default=str(DB_PATH), help='SQLite task database path.')
    parser.add_argument('--format', choices=['json', 'csv'], default='json', help='Output format.')
    parser.add_argument('--output', help='Write output to a file instead of stdout.')
    parser.add_argument('--gui', action='store_true', help='Launch the desktop UI.')

    actions = parser.add_mutually_exclusive_group()
    actions.add_argument('--list', action='store_true', help='List tasks.')
    actions.add_argument('--next', action='store_true', help='List next incomplete tasks.')
    actions.add_argument('--get', type=int, metavar='ID', help='Get one task by id.')
    actions.add_argument('--add', action='store_true', help='Insert one task from flags.')
    actions.add_argument('--update', type=int, metavar='ID', help='Update one task from flags.')
    actions.add_argument('--delete', type=int, metavar='ID', help='Delete one task.')
    actions.add_argument('--upload', help='Apply insert/update/delete records from CSV or JSON.')
    actions.add_argument('--export', help='Export tasks to the supplied CSV or JSON path.')
    actions.add_argument('--stats', action='store_true', help='Return task counts by status.')

    parser.add_argument('--title')
    parser.add_argument('--description', default='')
    parser.add_argument('--status', type=int)
    parser.add_argument('--priority', type=int)
    parser.add_argument('--effort', type=int)
    parser.add_argument('--scope', default='')
    parser.add_argument('--phase', default='')
    parser.add_argument('--search', default='')
    parser.add_argument('--sort', default='priority')
    parser.add_argument('--desc', action='store_true')
    parser.add_argument('--limit', type=int, default=20)
    return parser

def run_cli(argv):
    global db
    parser = build_cli_parser()
    args = parser.parse_args(argv)

    DB.configure_path(args.db)
    db = DB.get()

    if args.upload:
        summary = db.apply_records(read_records(args.upload))
        write_output(summary, args.format, args.output)
        return 0 if not summary["errors"] else 1

    if args.add:
        task_id = db.add_task(
            title=args.title or '',
            description=args.description or '',
            status=args.status or 1,
            priority=args.priority or 5,
            effort=args.effort or 0,
            scope=args.scope or '',
            phase=args.phase or '',
        )
        write_output(db.get_task(task_id), args.format, args.output)
        return 0

    if args.update is not None:
        fields = {
            'title': args.title,
            'description': args.description if args.description else None,
            'status': args.status,
            'priority': args.priority,
            'effort': args.effort,
            'scope': args.scope if args.scope else None,
            'phase': args.phase if args.phase else None,
        }
        ok = db.update_task(args.update, **fields)
        payload = db.get_task(args.update) if ok else {"error": "task not found", "id": args.update}
        write_output(payload, args.format, args.output)
        return 0 if ok else 1

    if args.delete is not None:
        ok = db.delete_task(args.delete)
        write_output({"deleted": ok, "id": args.delete}, args.format, args.output)
        return 0 if ok else 1

    if args.get is not None:
        task = db.get_task(args.get)
        write_output(task or {"error": "task not found", "id": args.get}, args.format, args.output)
        return 0 if task else 1

    if args.next:
        write_output(db.get_whats_next(args.limit), args.format, args.output)
        return 0

    if args.stats:
        counts, total = db.stats()
        write_output({"total": total, "by_status": counts}, args.format, args.output)
        return 0

    if args.export:
        export_format = 'json' if args.export.lower().endswith('.json') else 'csv'
        write_output(db.get_tasks(), export_format, args.export)
        write_output({"exported": args.export}, args.format, args.output)
        return 0

    write_output(
        db.get_tasks(
            status_filter=args.status or 0,
            scope_filter=args.scope or '',
            search=args.search or '',
            sort_col=args.sort,
            sort_asc=not args.desc,
        ),
        args.format,
        args.output,
    )
    return 0

# ─── ENTRY POINT ─────────────────────────────────────────────────────────────

def main():
    global db
    db = DB.get()
    if not GUI_AVAILABLE:
        print("PyQt5 is not installed. Use CLI flags such as --list, or install PyQt5 for the desktop UI.", file=sys.stderr)
        sys.exit(1)

    app = QApplication(sys.argv)
    app.setApplicationName("TaskMan")
    app.setStyle("Fusion")

    # Dark palette base (QSS handles the rest)
    palette = QPalette()
    palette.setColor(QPalette.Window, QColor("#0F1117"))
    palette.setColor(QPalette.WindowText, QColor("#E2E8F0"))
    palette.setColor(QPalette.Base, QColor("#1A1D27"))
    palette.setColor(QPalette.AlternateBase, QColor("#121520"))
    palette.setColor(QPalette.Text, QColor("#E2E8F0"))
    palette.setColor(QPalette.Button, QColor("#1A1D27"))
    palette.setColor(QPalette.ButtonText, QColor("#E2E8F0"))
    palette.setColor(QPalette.Highlight, QColor("#6366F1"))
    palette.setColor(QPalette.HighlightedText, QColor("#FFFFFF"))
    app.setPalette(palette)

    win = MainWindow()
    win.show()
    sys.exit(app.exec_())

if __name__ == "__main__":
    if len(sys.argv) > 1 and "--gui" not in sys.argv:
        sys.exit(run_cli(sys.argv[1:]))
    sys.argv = [arg for arg in sys.argv if arg != "--gui"]
    main()
