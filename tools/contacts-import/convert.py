"""Turns the customer export into the contact seed file.

    py -m pip install openpyxl
    py tools/contacts-import/convert.py

Reads docs/Contacts.xlsx - the "ExportResult" sheet from the old ordering
system - and writes

    src/CallCenter.Server/Data/Seed/Contacts/contacts.csv.gz

which is embedded in CallCenter.Server.dll and read by DatabaseSeeder at
install time. Run it again when a newer export arrives, and commit the result.

What it does, and what it deliberately does not:

* Only five columns survive: Customer, Phone, Phone2, Address+City, Notes.
  The order statistics (F. Order, L. Order, # Orders, T Orders, T 30D) have
  nowhere to live in our schema, and the BL / Reason / Action columns are
  empty in every row of this export.
* The city is appended to the address, because 1,369 of these customers are
  in Jerusalem and an address without its city is not deliverable. Known
  misspellings are folded (Ramallah / رامالله / را م الله -> رام الله);
  anything unrecognised is passed through untouched rather than guessed at.
* Phone numbers are left exactly as exported. They are normalised by
  CallCenter.Shared.Phone.PhoneNormalizer at seed time, so there is one set
  of rules for these rows and for a number an agent types, not two.
* Duplicates are not resolved here either - the seeder drops a number it has
  already inserted, for the same reason.
"""

import csv
import gzip
import io
import re
import sys
from pathlib import Path

try:
    import openpyxl
except ImportError:
    sys.exit("openpyxl is needed:  py -m pip install openpyxl")

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "docs" / "Contacts.xlsx"
TARGET = ROOT / "src" / "CallCenter.Server" / "Data" / "Seed" / "Contacts" / "contacts.csv.gz"

SHEET = "ExportResult"
HEADER = ["name", "phone", "phone2", "address", "notes"]

# Column positions in the export, which has no stable column ids.
CUSTOMER, PHONE, PHONE2, ADDRESS, CITY, NOTES = 1, 2, 3, 4, 5, 6

# Spellings of the three cities that actually appear, folded to one form each.
# Everything else in the City column is left as the shop typed it.
CITIES = {
    "رام الله": ["ramallah", "ramallh", "رامالله", "رام لله", "را م الله", "رام االله", "رام تااه"],
    "القدس": ["jerusalem"],
    "نابلس": ["نايلس"],
}
CITY_FIXES = {variant: canonical for canonical, variants in CITIES.items() for variant in variants}
CITY_FIXES.update({canonical.casefold(): canonical for canonical in CITIES})


def clean(value):
    """Trims, collapses runs of whitespace, and turns blanks into ''."""
    if value is None:
        return ""
    return re.sub(r"\s+", " ", str(value)).strip()


def city_of(value):
    cleaned = clean(value)
    return CITY_FIXES.get(cleaned.casefold(), cleaned)


def address_of(address, city):
    """The address with its city, unless the address already says it."""
    address, city = clean(address), city_of(city)

    if not city or city.casefold() in address.casefold():
        return address

    return f"{address} - {city}" if address else city


def main():
    if not SOURCE.exists():
        sys.exit(f"not found: {SOURCE}")

    workbook = openpyxl.load_workbook(SOURCE, read_only=True, data_only=True)

    if SHEET not in workbook.sheetnames:
        sys.exit(f"{SOURCE.name} has no '{SHEET}' sheet; found {workbook.sheetnames}")

    rows = workbook[SHEET].iter_rows(min_row=2, values_only=True)

    buffer = io.StringIO(newline="")
    writer = csv.writer(buffer, lineterminator="\n")
    writer.writerow(HEADER)

    written = skipped = 0

    for row in rows:
        name = clean(row[CUSTOMER])
        phone = clean(row[PHONE])
        phone2 = clean(row[PHONE2])

        # A row with neither a name nor a number is not a customer.
        if not name and not phone and not phone2:
            skipped += 1
            continue

        writer.writerow([
            name,
            phone,
            phone2 if phone2 != phone else "",
            address_of(row[ADDRESS], row[CITY]),
            clean(row[NOTES]),
        ])
        written += 1

    TARGET.parent.mkdir(parents=True, exist_ok=True)

    # mtime=0 so that re-running without a new export produces an identical
    # file, and git shows no change.
    with gzip.GzipFile(TARGET, "wb", compresslevel=9, mtime=0) as out:
        out.write(buffer.getvalue().encode("utf-8"))

    print(f"{written} contacts -> {TARGET.relative_to(ROOT)} ({TARGET.stat().st_size / 1024:.0f} KB)")

    if skipped:
        print(f"{skipped} empty row(s) skipped")


if __name__ == "__main__":
    main()
