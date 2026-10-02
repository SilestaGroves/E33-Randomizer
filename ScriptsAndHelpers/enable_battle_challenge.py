"""Enables battle challenges (damage limit, enemy life multiplier) in an Expedition 33 save file.

Usage: python enable_battle_challenge.py "path_to_save.sav" <damage_limit> <life_multiplier>
Pass anything else (e.g. NONE) to turn a challenge off.

Needs uesave.exe next to this script, in the current folder, or in ../tools.
Works with the JSON formats of uesave 0.7+ and of older versions.
The original save is kept next to it as .sav.bak.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
from sys import argv

damage_limits = ['LIMIT_100K', 'LIMIT_1M']
life_multipliers = ['MULT_2', 'MULT_5', 'MULT_10', 'MULT_20', 'MULT_50', 'MULT_100']

DIFFICULTY_PROPERTY = 'GameDifficultyData_0'
CHALLENGES_FIELD = 'ActivatedChallengeIDs_12_D7F8D90845065AFBB7E341AA177A43A9'
CHALLENGES_TYPE = {"data": {"Map": {"key_type": {"Other": "NameProperty"}, "value_type": {"Other": "NameProperty"}}}}


def find_uesave() -> str:
    script_dir = os.path.dirname(os.path.abspath(__file__))
    for folder in (script_dir, os.getcwd(), os.path.join(script_dir, "..", "tools")):
        path = os.path.join(folder, "uesave.exe")
        if os.path.isfile(path):
            return os.path.abspath(path)
    return None


def get_challenges(damage_limit: str, life_mult: str) -> list:
    challenges = []
    if damage_limit in damage_limits:
        challenges.append(('BATTLECHALLENGE_LIMITDAMAGE', damage_limit))
    if life_mult in life_multipliers:
        challenges.append(('BATTLECHALLENGE_ENEMYLIFEMULT', life_mult))
    return challenges


def patch_json(save_obj: dict, damage_limit: str, life_mult: str) -> dict:
    properties = save_obj['root']['properties']
    if DIFFICULTY_PROPERTY not in properties:
        raise ValueError('This save has no difficulty data. It was probably made with a game version without battle '
                         'challenges: load it in the current version of the game and save again.')

    challenges = get_challenges(damage_limit, life_mult)
    field = CHALLENGES_FIELD + '_0'

    if 'schemas' in save_obj:
        # uesave 0.7+: struct fields are plain values, the map type lives in "schemas"
        difficulty_struct = properties[DIFFICULTY_PROPERTY]
        if challenges:
            difficulty_struct[field] = [{'key': key, 'value': value} for key, value in challenges]
            schema_path = DIFFICULTY_PROPERTY[:-2] + '.' + CHALLENGES_FIELD
            save_obj['schemas'].setdefault('schemas', {}).setdefault(schema_path, CHALLENGES_TYPE)
        else:
            difficulty_struct.pop(field, None)
    else:
        difficulty_struct = properties[DIFFICULTY_PROPERTY]['Struct']['Struct']
        if challenges:
            difficulty_struct[field] = {
                'tag': CHALLENGES_TYPE,
                'Map': [{'key': {'Name': key}, 'value': {'Name': value}} for key, value in challenges],
            }
        else:
            difficulty_struct.pop(field, None)

    return save_obj


def run_uesave(uesave: str, *arguments: str) -> None:
    result = subprocess.run([uesave, *arguments], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"uesave failed with exit code {result.returncode}: {result.stderr.strip()}")


def patch(save_file_path: str, damage_limit: str, life_mult: str, uesave: str = None) -> None:
    uesave = uesave or find_uesave()
    json_path = os.path.join(tempfile.gettempdir(), f"e33rando_challenge_{os.getpid()}.json")
    try:
        run_uesave(uesave, "to-json", "-i", save_file_path, "-o", json_path)
        with open(json_path, encoding="utf-8") as f:
            save_obj = json.load(f)
        with open(json_path, "w", encoding="utf-8") as f:
            json.dump(patch_json(save_obj, damage_limit, life_mult), f, indent=2)

        shutil.copyfile(save_file_path, save_file_path + ".bak")
        run_uesave(uesave, "from-json", "-i", json_path, "-o", save_file_path)
    finally:
        if os.path.exists(json_path):
            os.remove(json_path)


if __name__ == '__main__':
    if not find_uesave():
        print('uesave.exe not found, please put it in the same directory as this script.')
        input('Press enter to exit...')
        exit()
    if len(sys.argv) != 4:
        print('Please indicate the save file path, damage limit, and life multiplier.')
        input('Press enter to exit...')
        exit()
    filename = argv[1]
    damage_limit_arg = argv[2]
    life_mult_arg = argv[3]
    try:
        patch(filename, damage_limit_arg, life_mult_arg)
        print(f'Patched. The original save was kept as {filename}.bak')
    except ValueError as error:
        print(error)
        input('Press enter to exit...')


# Damage limits options:
#  LIMIT_100K, LIMIT_1M

# Life multipliers:
#  MULT_2, MULT_5, MULT_10, MULT_20, MULT_50, MULT_100
