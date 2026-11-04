# Interrupted tooling transactions

Kit synchronization and application upgrades share one operation lock and a
transaction journal. Before replacing files, the CLI stores their original and
incoming bytes under `.ngb/transactions/<id>/objects/`, checks their SHA-256 hashes,
and writes `.ngb/transaction.json`. Completed receipts stay in that transaction
directory. Keep this private recovery directory until the update has been reviewed.

A handled write failure restores the files already changed. An abrupt process
exit can leave a prepared transaction and an operation lock. Inspect it with:

```sh
node infrastructure/ngb.mjs recover --format json
```

The preview does not modify application files. After reviewing the listed paths,
run `node infrastructure/ngb.mjs recover --apply`. A prepared transaction rolls
back; a transaction whose commit or rollback finished only needs its receipt
archived. Running recovery again is harmless.

Recovery verifies all backup bytes before writing, checks that every current file
still matches either its original or incoming hash, and restores original file
permissions. Subsequent edits, missing/corrupt backups, symlinks and unknown
journal fields stop recovery without overwriting the conflicting work. Preserve
that work separately and compare it with the saved bytes before resolving it.

Automatic lock recovery requires a recorded owner on this machine whose process
has exited. A running or reused process ID, a foreign host or a lock without an
owner requires manual inspection; do not delete a lock while another operation
may still be active. Process-crash recovery does not replace filesystem backups
or promise recovery from storage corruption or power loss.

The journal covers source/package/tooling files only. It never rolls back a
database migration or changes a running deployment. Deployment retains its
separate tested-backup requirement.
