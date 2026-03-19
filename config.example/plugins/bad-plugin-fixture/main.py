#!/usr/bin/env python3
"""
Intentionally broken plugin for testing fault isolation.
This plugin hangs for 60 seconds and then raises an exception.
The plugin engine should timeout and kill this process without affecting the main app.
"""
import time
import sys

# Simulate a hung plugin
time.sleep(60)

# If somehow we get past the timeout, crash hard
raise RuntimeError("This plugin is intentionally broken!")
