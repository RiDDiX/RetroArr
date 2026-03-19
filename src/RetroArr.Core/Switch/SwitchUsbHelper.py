#!/usr/bin/python3
try:
    import usb.core
    import usb.util
except ImportError as e:
    print(json.dumps({"error": f"ImportError: {str(e)}. Check if pyusb is installed in the python environment."}))
    sys.exit(1)

import struct
import sys
import time
import json
import argparse
import os

# Constants from dbibackend
CMD_ID_EXIT = 0
CMD_ID_FILE_RANGE = 2
CMD_ID_LIST = 3

CMD_TYPE_REQUEST = 0
CMD_TYPE_RESPONSE = 1
CMD_TYPE_ACK = 2

BUFFER_SEGMENT_DATA_SIZE = 0x100000

# Global state
in_ep = None
out_ep = None
current_file_path = None

def log_json(status, progress=None, error=None):
    msg = {"status": status}
    if progress is not None:
        msg["progress"] = progress
    if error is not None:
        msg["error"] = error
    print(json.dumps(msg), flush=True)

def connect_to_switch():
    global in_ep, out_ep
    dev = usb.core.find(idVendor=0x057E, idProduct=0x3000)
    if dev is None:
        return False

    try:
        # On macOS, reset() often requires exclusive access or fails with permissions.
        # Try to avoid it if possible, or catch it.
        try:
            dev.reset()
        except Exception as e:
            log_json("Warning: Reset failed (ignoring)", error=str(e))

        # Detach kernel driver - macOS often handles this differently or throws NotImplemented error
        if sys.platform != 'darwin':
            try:
                if dev.is_kernel_driver_active(0):
                    dev.detach_kernel_driver(0)
            except Exception as e:
                 # Ignore if not implemented or failed
                 pass
            
        # Set configuration
        # If it's already configured, setting it again might cause permission error on some platforms
        try:
            cfg = dev.get_active_configuration()
            if cfg is None or cfg.bConfigurationValue != 1:
                dev.set_configuration()
                cfg = dev.get_active_configuration()
        except Exception as e:
             # Fallback: force set
             try:
                dev.set_configuration()
                cfg = dev.get_active_configuration()
             except Exception as ex:
                log_json("Configuration Failed", error=f"Set Config: {str(ex)}")
                return False

        if cfg is None:
             log_json("Configuration Failed", error="Could not get active configuration")
             return False
        
        # Find interface interface settings
        intf = cfg[(0,0)]
        
        is_out = lambda ep: usb.util.endpoint_direction(ep.bEndpointAddress) == usb.util.ENDPOINT_OUT
        is_in = lambda ep: usb.util.endpoint_direction(ep.bEndpointAddress) == usb.util.ENDPOINT_IN
        
        out_ep = usb.util.find_descriptor(intf, custom_match=is_out)
        in_ep = usb.util.find_descriptor(intf, custom_match=is_in)
        
        if out_ep is None or in_ep is None:
            return False
            
        return True
    except Exception as e:
        log_json("Error connecting", error=str(e))
        return False

def list_devices():
    devices = []
    try:
        all_devs = usb.core.find(find_all=True)
        for dev in all_devs:
            if dev.idVendor == 0x057E:
                pid = dev.idProduct
                mode = "DBI Backend" if pid == 0x3000 else "MTP/Regular"
                devices.append(f"Nintendo Switch ({pid:04X} - {mode})")
        print(json.dumps({"devices": devices}))
    except Exception as e:
        print(json.dumps({"error": str(e)}))

def process_file_range_command(data_size):
    global current_file_path
    
    # ACK
    out_ep.write(struct.pack('<4sIII', b'DBI0', CMD_TYPE_ACK, CMD_ID_FILE_RANGE, data_size))

    # Read request header
    file_range_header = in_ep.read(data_size)
    range_size = struct.unpack('<I', file_range_header[:4])[0]
    range_offset = struct.unpack('<Q', file_range_header[4:12])[0]
    nsp_name_len = struct.unpack('<I', file_range_header[12:16])[0]
    nsp_name = bytes(file_range_header[16:]).decode('utf-8')

    # log_json(f"Sending range: {range_offset} - {range_offset + range_size}")

    # Send response header
    response_bytes = struct.pack('<4sIII', b'DBI0', CMD_TYPE_RESPONSE, CMD_ID_FILE_RANGE, range_size)
    out_ep.write(response_bytes)

    # Wait for final ACK from switch before sending data
    ack = bytes(in_ep.read(16, timeout=None))
    # ignoring ack content checks for speed, assume it's valid if we got here

    with open(current_file_path, 'rb') as f:
        f.seek(range_offset)
        curr_off = 0
        end_off = range_size
        read_size = BUFFER_SEGMENT_DATA_SIZE
        
        # Report progress periodically? 
        # Actually DBI requests ranges, so we can report progress based on range_offset
        total_size = os.path.getsize(current_file_path)
        progress = min(100.0, (float(range_offset + range_size) / float(total_size)) * 100.0)
        log_json("Installing", progress=progress)

        while curr_off < end_off:
            if curr_off + read_size >= end_off:
                read_size = end_off - curr_off

            buf = f.read(read_size)
            out_ep.write(data=buf, timeout=None)
            curr_off += read_size

def process_list_command():
    # When Switch asks for list of files
    if current_file_path:
        filename = os.path.basename(current_file_path)
        nsp_path_list = filename + '\n'
    else:
        nsp_path_list = ""
        
    nsp_path_list_bytes = nsp_path_list.encode('utf-8')
    nsp_path_list_len = len(nsp_path_list_bytes)

    # Send Response Header
    out_ep.write(struct.pack('<4sIII', b'DBI0', CMD_TYPE_RESPONSE, CMD_ID_LIST, nsp_path_list_len))

    if nsp_path_list_len > 0:
        # Wait for ACK
        ack = bytes(in_ep.read(16, timeout=None))
        # Send List
        out_ep.write(nsp_path_list_bytes)

def install_game(file_path):
    global current_file_path, in_ep
    current_file_path = file_path
    
    if not os.path.exists(file_path):
        log_json("Error", error="File not found")
        sys.exit(1)

    log_json("Waiting for Switch...")
    
    # Wait for connection
    while not connect_to_switch():
        time.sleep(1)
        
    log_json("Connected")
    
    
    # Command Loop
    files_installed = False
    
    while True:
        try:
            # log_json("Debug", error="Waiting for command header...")
            cmd_header_raw = in_ep.read(16, timeout=None)
            cmd_header = bytes(cmd_header_raw)
            
            if len(cmd_header) < 16:
                # log_json("Debug", error=f"Received short header: {len(cmd_header)} bytes")
                continue

            magic = cmd_header[:4]
            if magic != b'DBI0':
                # log_json("Debug", error=f"Invalid Magic: {magic}")
                continue

            cmd_type = struct.unpack('<I', cmd_header[4:8])[0]
            cmd_id = struct.unpack('<I', cmd_header[8:12])[0]
            data_size = struct.unpack('<I', cmd_header[12:16])[0]

            # log_json("Debug", error=f"CMD RECV: ID={cmd_id} TYPE={cmd_type} SIZE={data_size}")

            if cmd_id == CMD_ID_EXIT:
                out_ep.write(struct.pack('<4sIII', b'DBI0', CMD_TYPE_RESPONSE, CMD_ID_EXIT, 0))
                if files_installed:
                    log_json("Installation Completed", progress=100.0)
                else:
                    log_json("Status", error="Switch disconnected before installation started.")
                break
            elif cmd_id == CMD_ID_FILE_RANGE:
                process_file_range_command(data_size)
                files_installed = True
            elif cmd_id == CMD_ID_LIST or cmd_id == 1: # 1 = CMD_ID_LIST_OLD
                process_list_command()
                
        except usb.core.USBError as e:
            if e.errno == 60: # Time out
                 continue
            log_json("Connection Lost", error=f"USBError: {str(e)}")
            break
        except Exception as e:
            log_json("Error", error=f"Loop Exception: {str(e)}")
            break

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--list", action="store_true", help="List connected valid devices")
    parser.add_argument("--install", type=str, help="Install the specified NSP/NSZ/XCI file")
    
    args = parser.parse_args()
    
    if args.list:
        list_devices()
    elif args.install:
        install_game(args.install)
    else:
        # Default behavior if no args?
        print("Usage: --list or --install <file>")

if __name__ == "__main__":
    main()
