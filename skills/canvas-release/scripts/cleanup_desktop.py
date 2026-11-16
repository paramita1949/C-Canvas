"""Delete only the verified Desktop package after a complete release ledger."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sys
from datetime import datetime, timezone

GATES = ('source_verification', 'github_push', 'ci_asset', 'reactor_protection',
         'protected_runtime', 'dll_only_zip', 'r2_upload', 'r2_verify',
         'retention3', 'site_tests', 'site_deploy', 'site_live')

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def require(condition, message):
    if not condition:
        raise ValueError(message)

def absolute_file(value):
    path = Path(value)
    require(path.is_absolute() and path.is_file() and not path.is_symlink(),
            f'Expected an absolute regular file: {path}')
    return path.resolve()

def matching_file(path, expected):
    require(re.fullmatch(r'[0-9a-fA-F]{64}', expected or '') is not None,
            'Invalid SHA256')
    require(digest(path) == expected.lower(), f'SHA256 mismatch: {path}')

def cleanup(manifest_path, desktop, receipt_path):
    manifest_path = absolute_file(str(manifest_path))
    data = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    require(re.fullmatch(r'\d+(\.\d+){3}', data.get('version', '')) is not None,
            'Invalid release version')
    require(re.fullmatch(r'[0-9a-fA-F]{40}', data.get('commit', '')) is not None,
            'Invalid release commit')
    package = absolute_file(data['package_path'])
    project = absolute_file(data['project_path'])
    matching_file(package, data['package_sha256'])
    matching_file(project, data['project_sha256'])
    require(project.name == '123.nrproj', 'Original Reactor project required')
    for name in GATES:
        gate = data.get('gates', {}).get(name, {})
        require(gate.get('status') == 'pass', f'Unfinished release gate: {name}')
        evidence = absolute_file(gate.get('evidence_path', ''))
        matching_file(evidence, gate.get('evidence_sha256'))
        event = json.loads(evidence.read_text(encoding='utf-8-sig'))
        require(isinstance(event.get('command'), list) and bool(event['command'])
                and type(event.get('exit_status')) is int and event['exit_status'] == 0
                and isinstance(event.get('stdout'), str)
                and isinstance(event.get('stderr'), str),
                f'Missing successful command_execution: {name}')
    require(desktop.is_dir() and not desktop.is_symlink(), 'Desktop directory unavailable')
    desktop = desktop.resolve()
    target = desktop / 'Canvas-Update.zip'
    require(not target.is_symlink(), 'Refusing symbolic-link package')
    receipt_path = receipt_path.absolute()
    require(receipt_path.resolve() not in (target, package, project, manifest_path),
            'Receipt must not overwrite inputs')
    identity = dict(version=data['version'], commit=data['commit'],
                    package_sha256=data['package_sha256'].lower(),
                    manifest_sha256=digest(manifest_path), target_path=str(target))
    require(not receipt_path.is_symlink(), 'Refusing symbolic-link receipt')
    if not target.exists():
        require(receipt_path.is_file(), 'Package absent without verified cleanup receipt')
        old = json.loads(receipt_path.read_text(encoding='utf-8'))
        require(old.get('status') == 'deleted' and all(old.get(k) == v for k, v in identity.items()),
                'Cleanup receipt belongs to another release or is incomplete')
        return dict(identity, status='already_cleaned')
    require(not receipt_path.exists(), 'Receipt already exists; preserve evidence and use a new receipt path')
    require(target.is_file() and target.resolve().parent == desktop,
            'Expected exact regular Desktop/Canvas-Update.zip')
    require(target.resolve() != package and not os.path.samefile(target, package),
            'Preserve a separate archived package before cleanup')
    matching_file(target, data['package_sha256'])
    # Prepare receipt in writable evidence directory before deleting; never cleanup in finally.
    receipt_path.parent.mkdir(parents=True, exist_ok=True)
    receipt = dict(identity, status='prepared', bytes=target.stat().st_size,
                   checked_utc=datetime.now(timezone.utc).isoformat())
    # Exclusive creation also prevents overwriting an existing hardlink or evidence file.
    with receipt_path.open('x', encoding='utf-8') as stream:
        stream.write(json.dumps(receipt, indent=2))
    matching_file(target, data['package_sha256'])
    target.unlink()
    require(not target.exists(), 'Desktop package still exists')
    matching_file(project, data['project_sha256'])
    matching_file(package, data['package_sha256'])
    receipt['status'] = 'deleted'
    receipt_path.write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    return receipt

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--receipt', required=True, type=Path)
    parser.add_argument('--desktop', type=Path, default=Path.home() / 'Desktop')
    args = parser.parse_args()
    try:
        print(json.dumps(cleanup(args.manifest, args.desktop, args.receipt), indent=2))
        return 0
    except (ValueError, KeyError, OSError, TypeError) as error:
        print(f'CLEANUP_BLOCKED: {error}', file=sys.stderr)
        return 1

if __name__ == '__main__':
    raise SystemExit(main())
